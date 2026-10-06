# Validation

Executed in this workspace:

- Local Git workflow with a non-default branch (`release/cd`) and a shallow recursive clone.
- Copy of the 12 expected chart/value files and replacement of all three template-name variants.
- YAML parsing of generated test values: image tag is `1.0.0` in every test environment.
- Original template bytes and unrelated repository content remain unchanged.
- Only the new sensor directory is committed; the original two commits remain in history.
- An existing destination is rejected without overwriting.
- Two concurrent remote advances, two rejected pushes, fetch/unshallow/rebase then a successful retry. Both other writers' changes remain in the final history. The second refresh handles an already unshallowed repository.
- Supplemental source order check: sensor push precedes CD provisioning, which precedes the success response.
- Configuration JSON validation.
- Structured logger placeholder/argument count checks after adding stage, timing and failure diagnostics.

These are Git workflow checks using the included Python script, not execution of the C# service.

Not executed here:

- C# compilation and the .NET test suite: this workspace has no .NET SDK.
- OpenShift/GitLab integration: no access to the user's internal services, current repository, CD project ID, branch or actual YAML bytes.

C# checks are supplied for the real service and real GitRepository implementation against a temporary local Git remote. They cover copy isolation, initial tags from the template, retained history, duplicate/missing templates, cleanup, repeated remote advances and precise generation failure reporting. Additional C# checks verify sanitized Git stderr is kept in the exception chain and log messages, without configured Token/ApiKey values. These C# checks were not run here. Run them after merging into the current code, using the commands in INTEGRATION-STEP-BY-STEP-he.md.

No remote repositories or OpenShift resources were changed.
