#!/usr/bin/env python3
"""Local Git workflow verification. Does not compile or execute the C# service."""
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile

import yaml

BRANCH = "release/cd"
FILES = ["Chart.yaml", "values.yaml", "develop-values.yaml", "integration-values.yaml", "production-values.yaml", "secondary-values.yaml"]


def git(directory, *arguments, fail=False):
    env = dict(os.environ, GIT_CONFIG_NOSYSTEM="1", GIT_CONFIG_GLOBAL="/dev/null", GIT_TERMINAL_PROMPT="0")
    p = subprocess.run(["git", "-c", "core.hooksPath=/dev/null", *arguments], cwd=directory, env=env,
                       text=True, capture_output=True)
    if fail:
        assert p.returncode != 0, "expected a rejected concurrent push"
    elif p.returncode:
        raise AssertionError(p.stderr)
    return p.stdout.strip()


def author(directory):
    git(directory, "config", "user.name", "Component Factory")
    git(directory, "config", "user.email", "factory@example.invalid")


def clone(root, remote, name):
    path = root / name
    git(root, "check-ref-format", "--branch", BRANCH)
    git(root, "clone", "--recurse-submodules", "--depth", "1", "--single-branch", "--branch", BRANCH,
        "--", remote.as_uri(), str(path))
    author(path)
    return path


def add_sensor(repo, name):
    source = repo / "sensorgates/senortemplate"
    target = repo / "sensorgates" / name.lower()
    if target.exists():
        raise FileExistsError(target)
    shutil.copytree(source, target)
    replacements = {"SenorTemplate": name, "senortemplate": name.lower(), "SENORTEMPLATE": name.upper()}
    pattern = re.compile("|".join(map(re.escape, sorted(replacements, key=len, reverse=True))))
    for file in target.rglob("*"):
        if file.is_file():
            file.write_bytes(pattern.sub(lambda m: replacements[m.group()], file.read_bytes().decode("utf-8")).encode("utf-8"))
    relative = f"sensorgates/{name.lower()}"
    git(repo, "add", "--all", "--", relative)
    git(repo, "commit", "-m", f"Add CD charts for {name}")
    return target


def push(repo, remote, rejected=False):
    git(repo, "config", "remote.origin.url", remote.as_uri())
    git(repo, "push", "--set-upstream", "origin", f"HEAD:refs/heads/{BRANCH}", fail=rejected)


def refresh(repo):
    arguments = ["fetch", "--no-recurse-submodules"]
    if (repo / ".git/shallow").exists():
        arguments.append("--unshallow")
    git(repo, *arguments, "origin", BRANCH)
    git(repo, "rebase", "FETCH_HEAD")


def main():
    with tempfile.TemporaryDirectory(prefix="factory-cd-verification-") as directory:
        root = Path(directory)
        seed = root / "seed"
        seed.mkdir()
        git(seed, "init", f"--initial-branch={BRANCH}")
        author(seed)
        for role in ["agent", "poller"]:
            role_path = seed / "sensorgates/senortemplate" / role
            role_path.mkdir(parents=True)
            for file in FILES:
                text = f"apiVersion: v2\nname: senortemplate-{role}\nversion: 1.0.0\n" if file == "Chart.yaml" else (
                    f"generic-chart:\n  applicationName: senortemplate-{role}\n  deployment:\n    image:\n"
                    f"      repository: registry.example/senortemplate-{role}\n      tag: 1.0.0\n"
                    "  displayName: SenorTemplate\n  upperName: SENORTEMPLATE\n"
                )
                (role_path / file).write_text(text)
        git(seed, "add", "--all")
        git(seed, "commit", "-m", "existing templates")
        (seed / "unrelated.txt").write_text("do not change")
        git(seed, "add", "--all")
        git(seed, "commit", "-m", "existing content")
        remote = root / "cd.git"
        git(root, "clone", "--bare", str(seed), str(remote))
        old_tip = git(remote, "rev-parse", BRANCH)
        repo = clone(root, remote, "new-sensor")
        before = {str(p.relative_to(repo)): p.read_bytes() for p in (repo / "sensorgates/senortemplate").rglob("*") if p.is_file()}
        target = add_sensor(repo, "Bravo")
        assert len(list(target.rglob("*.yaml"))) == 12
        for role in ["agent", "poller"]:
            for file in FILES:
                text = (target / role / file).read_text()
                assert "senortemplate" not in text and "SenorTemplate" not in text and "SENORTEMPLATE" not in text
                if file.endswith("values.yaml"):
                    assert yaml.safe_load(text)["generic-chart"]["deployment"]["image"]["tag"] == "1.0.0"
        assert all((repo / path).read_bytes() == contents for path, contents in before.items())
        assert (repo / "unrelated.txt").read_text() == "do not change"
        changed = git(repo, "diff", "--name-only", "HEAD~1", "HEAD").splitlines()
        assert len(changed) == 12 and all(p.startswith("sensorgates/bravo/") for p in changed)
        push(repo, remote)
        assert git(remote, "rev-list", "--count", BRANCH) == "3"
        assert git(remote, "rev-parse", f"{BRANCH}~1") == old_tip
        print("PASS: 12 files, all names, initial tags, isolation and preserved CD history")

        try:
            add_sensor(repo, "Bravo")
        except FileExistsError:
            pass
        else:
            raise AssertionError("existing sensor folder must not be overwritten")
        print("PASS: existing sensor folder is not overwritten")

        delayed = clone(root, remote, "delayed")
        add_sensor(delayed, "Delta")
        for index in [1, 2]:
            writer = clone(root, remote, f"writer-{index}")
            (writer / f"advance-{index}.txt").write_text(f"remote change {index}")
            git(writer, "add", "--all")
            git(writer, "commit", "-m", f"concurrent change {index}")
            push(writer, remote)
            push(delayed, remote, rejected=True)
            refresh(delayed)
        push(delayed, remote)
        assert git(remote, "rev-list", "--count", BRANCH) == "6"
        for index in [1, 2]:
            assert git(remote, "show", f"{BRANCH}:advance-{index}.txt") == f"remote change {index}"
        assert "delta-agent" in git(remote, "show", f"{BRANCH}:sensorgates/delta/agent/Chart.yaml")
        print("PASS: two concurrent remote advances survive fetch/rebase/retry, without force-push")

    # Supplemental source checks. These are not a C# compilation or runtime test.
    code = Path(__file__).resolve().parent.parent / "cd-extension"
    if code.exists():
        generator = (code / "Application/ComponentGenerator.cs").read_text()
        body = generator.split("private CancellationTokenSource", 1)[0]
        assert body.index("PushToCreatedProjectAsync(workspace") < body.index("ProvisionCdAsync(name") < body.index("return new GeneratedComponent")
        json.loads((code / "appsettings.json").read_text())
        print("PASS: supplementary source flow order and JSON validation")

    print("Git workflow verification passed. C# service tests still require a .NET 10 SDK.")


if __name__ == "__main__":
    main()
