# ComponentFactory — .NET 10

Creates independent GitLab projects from the master branch of a dedicated template.
The template's Git history is discarded; the new project has one initial commit.
Image/metadata validation is intentionally deferred to the next stage.

## Code organization

The project uses one deployable ASP.NET Core application with separate folders
for responsibilities. There are no extra deployable services or layer projects.

| Folder | Responsibility | Main entry point |
| --- | --- | --- |
| Api | HTTP request/response, API key authentication, error mapping | ComponentsController |
| Application | Coordinates component generation through interfaces | ComponentGenerator |
| Application/Abstractions | Contracts for external operations and workspaces | IGitLabClient, IGitRepository, ITemplateCustomizer |
| Domain | Valid component names and application error types | ComponentName |
| Infrastructure/GitLab | GitLab HTTP requests and JSON DTOs | GitLabClient |
| Infrastructure/Git | Repository operations and process execution | GitRepository, GitCommandRunner |
| Infrastructure/Templates | Scan, validate rename plan, rewrite text, rename paths | TemplateCustomizer |
| Infrastructure/Workspaces | Temporary folder lifecycle and Git authentication helper | WorkspaceFactory |
| Configuration | Settings validation and dependency registration | ServiceCollectionExtensions |
| tests | Behavior checks with fake dependencies and real local Git | FactoryChecks |

Start reading at ComponentsController.CreateAsync, then
ComponentGenerator.GenerateAsync. The generator checks availability, prepares the
repository, creates the remote project, and pushes. It does not execute commands,
call HttpClient, or rewrite files itself. Its dependencies are constructor-injected
interfaces, so the workflow can be tested without a GitLab instance.

Template customization has distinct steps: scan supported files, validate path
collisions before changing anything, rewrite UTF-8 content, then rename children
before parent directories. NameReplacement centralizes the three spelling variants.

Application errors do not carry HTTP status codes. FactoryExceptionHandler maps
them to HTTP responses. API authentication is separate from the controller action.
.editorconfig keeps Allman braces and consistent spacing.

## Request

POST /api/components
Header: X-Api-Key: <your service key>
Content-Type: application/json

```json
{"componentName":"Bravo"}
```

The input is case-sensitive PascalCase (2–64 ASCII letters/digits). GitLab project
name/path: bravo. Code name: Bravo. Other variants: bravo and BRAVO.
This is a synchronous endpoint; give your HTTP client/proxy sufficient timeout.
Do not retry a timed-out request blindly: check whether the target project exists.

Success: 201 with projectId, webUrl, componentName, branch.
Conflict: 409. Invalid name/template collision: 400. Missing API key: 401.
Upstream/generation errors: 502. Pre-creation cancellation/timeout: 504.
A post-creation error includes projectUrl and stage. The project is retained.
A failed/ambiguous project creation HTTP response can still leave a project:
check the target GitLab group before retrying. No project is automatically deleted.

## Configuration

Set these through environment variables or your deployment's configuration:

- Factory__GitLabUrl: HTTPS URL of your GitLab instance.
- Factory__TemplateProjectId: numeric ID of the dedicated template project.
- Factory__NamespaceId: numeric ID of the exact target group/subgroup.
- Factory__TemplateName: exact PascalCase base token, e.g. TemplateSensor.
- Factory__Token: service-account GitLab token (OpenShift Secret).
- Factory__ApiKey: a separate key for callers of this API (OpenShift Secret).
- Factory__OperationTimeoutSeconds: default 300.

The GitLab identity needs API access, read access to the template, permission to
create private projects in the target group, and permission to push master.
Existing group restrictions or protected-branch policies can block the push.
Neither token is included in the repository URL. Git uses an executable askpass
helper containing no token; the token is supplied to the Git child environment.
Never put actual secrets in appsettings.json or commit them.

## Template contract

Create and push a nonempty master branch. Use exactly these three variants:
TemplateSensor, templatesensor, TEMPLATESENSOR. Replacement is literal and
single-pass in UTF-8 text and in file/directory names, including hidden CI files.
For example TemplateSensorAgent becomes BravoAgent and TemplateSensor.sln
becomes Bravo.sln. namespaces/project references are changed as text.

UTF-8 BOM and existing line endings are preserved. Binary and non-UTF-8 files
are not rewritten. Use UTF-8 for all configuration/source files needing replacement.
Mixed spellings such as templateSensor are not replaced. Use only the three
supported spellings. Avoid unrelated text containing the template token.
Symbolic links, submodules, and LFS templates are unsupported and rejected.
Do not include bin/obj, credentials, generated files, or environment-specific values.
.gitignore should not exclude any template source that must be committed.
CI may run automatically after the initial push: configure template CI accordingly.

## Build and run

```sh
dotnet build ComponentFactory.csproj
dotnet run --project tests/FactoryChecks.csproj
docker build -t YOUR_REGISTRY/component-factory:1.0 .
```

Use openshift.yaml after updating the image, GitLab URL, and IDs. Supply the
component-factory-secrets Secret separately (gitlab-token and api-key keys).
The container includes Git and runs without root. Work files live in an emptyDir
mounted at /tmp. The manifest creates an internal Service, not a public Route.
Acknowledge your cluster's trusted CA requirements if GitLab uses an internal CA;
install the CA in the image/system trust rather than disabling TLS verification.

Temporary workspaces are removed after success/failure. Pod termination may leave
files until the pod/emptyDir is replaced. Concurrent requests use isolated UUID
directories; GitLab is the final authority for duplicate project names.

## Validation

FactoryChecks is a dependency-free executable organized into named suites. It
checks renaming, preserved encodings, unsupported templates, local Git history,
GitLab request payloads/conflicts, configuration/name validation, and generation
success/failure/cleanup behavior through injected fake dependencies. It does not contact your GitLab. Verify a disposable project against
your real GitLab before deployment; group/CI/network policies cannot be verified here.

Verified after refactoring: build with zero errors/warnings, 48 behavior
assertions, and HTTP checks for health, API authentication and invalid input.
The Docker image and live OpenShift/GitLab environment were not exercised here.
