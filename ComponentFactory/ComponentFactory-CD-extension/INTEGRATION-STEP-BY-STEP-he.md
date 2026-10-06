# שילוב CD ולוגים ב־ComponentFactory — שלב אחרי שלב

## לפני שמתחילים

המטרה היא להרחיב את הפרויקט הקיים. סדר הפעולות נשאר:

**שם סנסור → יצירת הסנסור ו־push שלו → יצירת קובצי CD ו־push שלהם → תשובה.**

בתמונות שלך כבר יש `ILogger<T>` ושדה `_logger`. הקבצים משתמשים באותו סגנון. אין logger עצמאי, אין `Log.Logger` חדש ואין שינוי באתחול Serilog.

הקבצים הקיימים ב־`reference-existing` הם חומר להשוואה. בסיסם הוא העותק הישן שנשלח עם תיקון ה־submodules שסופק קודם; איני מחזיק את גרסת הקוד ששינית בעצמך מאז. **אל תדרוס את הקבצים המתוקנים שלך.** העתק את המתודות והשורות הדרושות לפי המדריך.

לפני עריכה, שמור את מצב הפרויקט אצלך ב־Git. פתח את הפרויקט הקיים ב־Visual Studio. לא צריך ליצור solution או project נוסף, ולא צריך ליצור פרויקט GitLab חדש עבור CD.

## שלב 1 — הוסף את ארבעת הקבצים החדשים

העתק את תוכן `new-files` לשורש פרויקט ComponentFactory, כך שהנתיבים יהיו:

| קובץ בפרויקט שלך | מה הוא עושה |
| --- | --- |
| `Application/Abstractions/ICdProvisioner.cs` | מגדיר את הפעולה `ProvisionAsync` |
| `Infrastructure/Cd/CdProvisioner.cs` | מנהל שכפול CD, העתקה, החלפת שמות, commit ו־push |
| `Configuration/CdOptions.cs` | מחזיק את הגדרות CD |
| `Configuration/CdOptionsValidator.cs` | בודק את ההגדרות בזמן עליית השירות |

אם תיקיית `Infrastructure/Cd` לא קיימת, צור אותה. namespaces כבר מוגדרים בקבצים. בפרויקט SDK רגיל הקבצים נכללים אוטומטית; אם אצלך יש רשימת Compile ידנית, הוסף אותם לרשימה הזאת.

ב־CdProvisioner כבר קיימים הבנאי עם `ILogger<CdProvisioner> logger` והשדה `_logger`. לא צריך להוסיף אותם שוב.

## שלב 2 — הרחב את חוזה ה־Git

פתח `Application/Abstractions/IGitRepository.cs`.

**בתוך הממשק**, אחרי המתודות הקיימות, הוסף את המתודות האלה אם הן עדיין לא קיימות:

```csharp
Task CloneAsync(
    GitLabProject project,
    IWorkspace workspace,
    string branch,
    CancellationToken cancellationToken);

Task CommitAsync(
    IWorkspace workspace,
    string message,
    string relativePath,
    CancellationToken cancellationToken);

Task PushAsync(
    IWorkspace workspace,
    GitLabProject target,
    string branch,
    CancellationToken cancellationToken);

Task RefreshBranchAsync(
    IWorkspace workspace,
    string branch,
    CancellationToken cancellationToken);
```

השאר את `CloneTemplateAsync`, `CreateInitialCommitAsync` ואת `PushAsync` הישן. השאר גם מתודות נוספות שיש בגרסה שלך. התוספת אינה דורשת למחוק מתודות קיימות.

ה־using ל־`Application.Models` מספק `GitLabProject`. `IWorkspace` נמצא באותו namespace של הממשק.

## שלב 3 — הוסף logger ל־GitRepository

פתח `Infrastructure/Git/GitRepository.cs`.

אם כבר יש בבנאי `ILogger<GitRepository>` ושדה `_logger`, השתמש בהם. אחרת הוסף לבנאי הקיים:

```csharp
ILogger<GitRepository> logger
```

ובתוך המחלקה, ליד שאר השדות:

```csharp
private readonly ILogger<GitRepository> _logger = logger;
```

לדוגמה, אם הבנאי שלך הוא primary constructor עם runner ו־validator:

```csharp
public sealed class GitRepository(
    GitCommandRunner commands,
    RepositoryUrlValidator urls,
    ILogger<GitRepository> logger) : IGitRepository
```

אם יש לך בנאי רגיל, הוסף בו `this._logger = logger;` בהתאם לסגנון הקיים. אם ה־using אינו קיים דרך implicit/global usings, הוסף `using Microsoft.Extensions.Logging;`.

ה־DI מספק `ILogger<GitRepository>` דרך מערך הלוגים שכבר קיים. אין צורך לרשום logger חדש ב־ServiceCollection.

## שלב 4 — שתף את מימוש פעולות ה־Git

עדיין ב־GitRepository:

### 4.1 — שכפול

הוסף את `CloneAsync` שבקובץ reference. היא מקבלת ענף, בודקת את כתובת הריפו ואת שם הענף, ומריצה את השכפול באמצעות `_commands` הקיים.

החלף **רק את גוף** `CloneTemplateAsync` הקיים ב־:

```csharp
return CloneAsync(template, workspace, RepositoryDefaults.Branch, cancellationToken);
```

אם חתימתה הייתה `async Task`, שנה אותה ל־`Task`, כי כעת היא מחזירה את המשימה ישירות.

דגל השכפול הרקורסיבי נמצא במימוש המשותף:

```text
git clone --recurse-submodules --depth 1 --single-branch --branch <branch> -- <url> <path>
```

שמור תוספות נחוצות שכבר עשית לפקודת השכפול שלך. אין להשתמש ב־`--remote-submodules`, ואין לבצע שינוי בתוכן submodules.

### 4.2 — commit רגיל

הוסף את `CommitAsync` מקובץ reference. היא משתמשת ב־`ConfigureCommitAuthorAsync` וב־`RunInRepositoryAsync` הקיימים:

```csharp
await ConfigureCommitAuthorAsync(workspace, cancellationToken);
await RunInRepositoryAsync(
    workspace, cancellationToken, "add", "--all", "--", relativePath);
await RunInRepositoryAsync(
    workspace, cancellationToken, "commit", "-m", message);
```

העתק גם את לוגי ה־Debug שבתחילת המתודה ובסופה.

בתוך `CreateInitialCommitAsync` שלך, השאר את הדרך שבה אתה יוצר את ההיסטוריה הראשונית ושומר את submodules. החלף את שלוש פעולות author/add/commit בקריאה משותפת:

```csharp
await CommitAsync(
    workspace,
    $"Initialize {name.Value} from component template",
    ".",
    cancellationToken);
```

אם יש פעולת שינוי שם ענף אחרי ה־commit, השאר אותה. **שירות CD לא קורא ל־CreateInitialCommitAsync**, ולא מאפס את ההיסטוריה של ריפו CD.

### 4.3 — push לענף

הוסף את `PushAsync` עם פרמטר `branch` מקובץ reference.

הפעולות המשותפות הן:

```csharp
_urls.Validate(target.RepositoryUrl);
await RunInRepositoryAsync(
    workspace, cancellationToken,
    "config", "remote.origin.url", target.RepositoryUrl);
await RunInRepositoryAsync(
    workspace, cancellationToken,
    "push", "--set-upstream", "origin", $"HEAD:refs/heads/{branch}");
```

ל־clone כבר יש origin, ולכן משתמשים ב־config ולא מוסיפים origin נוסף. אין `--force`.

ה־PushAsync הישן מחזיר:

```csharp
return PushAsync(workspace, target, RepositoryDefaults.Branch, cancellationToken);
```

העתק את לוגי ה־Debug עם ProjectId ו־Branch. אין צורך לרשום את כתובת הריפו או פרטי האימות.

### 4.4 — fetch ו־rebase

הוסף את `RefreshBranchAsync` מקובץ reference.

היא בונה את פקודת fetch במקום אחד, מוסיפה `--unshallow` רק אם `.git/shallow` קיים, ואז מריצה:

```text
git rebase FETCH_HEAD
```

העתק את שלושת לוגי ה־Information: תחילת fetch, תחילת rebase וסיום rebase. כך אפשר להבחין בכשל ב־fetch לעומת כשל ב־rebase.

## שלב 5 — הרחב את חוזה החלפת השמות

פתח `Application/Abstractions/ITemplateCustomizer.cs`.

השאר את המתודה הקיימת והוסף:

```csharp
Task CustomizeAsync(
    string rootPath,
    ComponentName name,
    string templateName,
    CancellationToken cancellationToken);
```

המטרה היא להעביר את שם תבנית CD בנפרד משם תבנית קוד הסנסור, תוך שימוש באותו מנגנון החלפה.

## שלב 6 — הרחב את TemplateCustomizer

פתח `Infrastructure/Templates/TemplateCustomizer.cs`.

### 6.1 — logger

הוסף לבנאי `ILogger<TemplateCustomizer> logger` ולשדות:

```csharp
private readonly ILogger<TemplateCustomizer> _logger = logger;
```

אם כבר יש לך logger, השתמש בו. **אין צורך להוסיף GitCommandRunner לבנאי אם הוא לא קיים בגרסה שלך.** השאר את התשתית שעובדת אצלך.

### 6.2 — overload משותף

העבר את גוף `CustomizeAsync` הקיים למתודה החדשה שמקבלת גם `string templateName`.

בתוך הגוף המשותף שנה:

```csharp
var replacement = new NameReplacement(_templateName, name);
```

ל־:

```csharp
var replacement = new NameReplacement(templateName, name);
```

כל יתר הסריקה, ההחרגות, שינוי שמות הנתיבים והטיפול ב־submodules נשארים כפי שהם אצלך.

גוף המתודה הישנה יהיה:

```csharp
return CustomizeAsync(repositoryPath, name, _templateName, cancellationToken);
```

הסר `async` מהחתימה הישנה אם היא רק מחזירה את המשימה הזאת.

### 6.3 — לוגים

בתחילת הגוף המשותף הוסף:

```csharp
_logger.LogDebug(
    "Customizing template {TemplateName} for {Sensor} under {RootPath}",
    templateName, name.Value, repositoryPath);
```

אחרי הסריקה אפשר לרשום `entries.Count`. אם יש בגרסה שלך `out submodulePaths`, השתמש גם בלוג מספר submodules מקובץ reference. אם לא, אין צורך להוסיף את ה־out רק בשביל הלוג.

בסיום הגוף המשותף הוסף:

```csharp
_logger.LogDebug(
    "Template customization completed for {Sensor} under {RootPath}",
    name.Value, repositoryPath);
```

אין החלפת שמות נוספת בשירות CD. הוא מפעיל את המתודה הזאת על התיקייה החדשה בלבד.

## שלב 7 — הוסף אבחון ב־GitCommandRunner

פתח `Infrastructure/Git/GitCommandRunner.cs`.

זה הקובץ הנוסף ששונה בעקבות בקשת הלוגים. בעותק הישן stderr נקרא אך לא נכנס ללוג או לחריגה. כך גם clone, commit או push שנכשלים מסתיימים בהודעה כללית.

### 7.1 — בנאי ושדות

הוסף לבנאי `ILogger<GitCommandRunner> logger` ולשדות:

```csharp
private readonly ILogger<GitCommandRunner> _logger = logger;
```

שמור את `IOptions<FactoryOptions>` ואת הגדרות האימות הקיימות. הוסף בראש הקובץ:

```csharp
using System.Text.RegularExpressions;
```

`using System.Diagnostics;` כבר היה קיים בעותק שנשלח. אם חסר אצלך, הוסף אותו לצורך Stopwatch ו־Process.

### 7.2 — גוף ההרצה

שלב את `RunAsync` מקובץ reference. בגרסה המצורפת היא:

1. בודקת שיש פקודה ושהבקשה לא בוטלה.
2. מתחילה Stopwatch ורושמת את שם פעולת Git ב־Debug.
3. מפעילה את Process באמצעות CreateStartInfo הקיים.
4. קוראת stdout ו־stderr במקביל, כפי שהיה קודם, כדי שלא תהיה חסימה על פלט.
5. מחכה לסיום באמצעות WaitForExitAsync הקיים.
6. בביטול רושמת Warning ומבצעת `throw;`, כך שהביטול נשמר.
7. בקוד יציאה שונה מ־0 מסננת את stderr, רושמת Error ושומרת את הטקסט המסונן ב־InnerException.
8. בהצלחה רושמת Debug עם זמן הריצה.

**שמור את CreateStartInfo, ConfigureEnvironment ו־WaitForExitAsync שלך** אם תיקנת אותם מאז. אין צורך להחליף טיפול ב־CA, GitAskPass או timeout שעובדים אצלך.

### 7.3 — שתי מתודות העזר

העתק מ־reference את:

```csharp
private string SanitizeDiagnostic(string text)
```

והחלף את CommandFailure בגרסה שמקבלת גם details:

```csharp
private static ComponentFactoryException CommandFailure(
    string operation, string details)
```

שתי הקריאות ב־RunAsync כבר מותאמות לחתימה החדשה. אם יש אצלך קריאות נוספות ל־CommandFailure, עדכן גם אותן.

SanitizeDiagnostic מסתירה את Token ו־ApiKey שהוגדרו, credentials ב־URL, ערכי Authorization וערכי token בכותרות/פרמטרים מוכרים. היא מגבילה את אורך פלט הכשל ל־4,000 תווים. היא אינה מנגנון הסתרה כללי לכל תוכן אפשרי ש־Git עשוי להוציא, ולכן אין להוסיף dump של משתני הסביבה או של כל שורת הפקודה.

הודעת החריגה הראשית נשארת כללית; הסיבה של Git נמצאת ב־InnerException ובלוג `GitError=`. כך ה־handler הקיים שלך, שכבר מדפיס Details ו־RootCause, מקבל מידע שימושי במקום לאבד אותו.

## שלב 8 — חבר את ComponentGenerator

פתח `Application/ComponentGenerator.cs`.

### 8.1 — הזרקה

הוסף לבנאי הקיים, למשל אחרי IWorkspaceFactory:

```csharp
ICdProvisioner cdProvisioner
```

הוסף לשדות:

```csharp
private readonly ICdProvisioner _cdProvisioner = cdProvisioner;
```

השאר את ILogger<ComponentGenerator> הקיים. לא צריך להוסיף logger נוסף.

### 8.2 — נקודת הקריאה

ב־GenerateAsync מצא את ה־push של הסנסור ואת `return new GeneratedComponent` שמופיע אחריו.

**ביניהם**, הוסף:

```csharp
await ProvisionCdAsync(name, project, operationToken);
```

הסדר צריך להיות:

```csharp
await PushToCreatedProjectAsync(workspace, project, operationToken);
await ProvisionCdAsync(name, project, operationToken);

return new GeneratedComponent(
    project.Id,
    project.WebUrl,
    name.Value,
    RepositoryDefaults.Branch);
```

שמור את בניית התשובה הנוכחית שלך אם יש בה שדות נוספים. הקריאה החדשה חייבת להיות לפני התשובה ואחרי push מוצלח.

### 8.3 — מתודת העזר

הוסף בתוך המחלקה את `ProvisionCdAsync` מקובץ reference, ליד PushToCreatedProjectAsync. היא:

- רושמת Information שהסנסור כבר פורסם וששלב CD מתחיל.
- מפעילה את `_cdProvisioner.ProvisionAsync` עם אותו operationToken.
- רושמת Information כשהסנסור וקובצי CD פורסמו והתשובה עומדת לחזור.
- בכשל רושמת את החריגה ומחזירה GenerationIncomplete עם כתובת הסנסור, שלב CD וכל שרשרת השגיאות.

אין צורך לשנות את ComponentsController, CreateComponentRequest או FactoryExceptionHandler.

## שלב 9 — רשום את השירות ואת ההגדרות

פתח `Configuration/ServiceCollectionExtensions.cs`.

בראש הקובץ הוסף:

```csharp
using ComponentFactory.Infrastructure.Cd;
```

בתוך `AddConfiguration`, אחרי רישום FactoryOptions, הוסף:

```csharp
services.AddSingleton<IValidateOptions<CdOptions>, CdOptionsValidator>();
services.AddOptions<CdOptions>()
    .BindConfiguration(CdOptions.SectionName)
    .ValidateOnStart();
```

בתוך `AddGenerationServices`, ליד רישום IComponentGenerator, הוסף:

```csharp
services.AddTransient<ICdProvisioner, CdProvisioner>();
```

השאר את הרישומים הקיימים ל־IGitRepository, ITemplateCustomizer, IWorkspaceFactory, GitCommandRunner ו־TemplateScanner. אין רישומים כפולים ואין runner נוסף.

ILogger<T> מוזרק אוטומטית דרך שירותי הלוגים שכבר רשומים אצלך. שמור את אתחול Serilog ב־Program.cs כפי שהוא.

## שלב 10 — הוסף את הגדרות CD

פתח appsettings.json והוסף את סעיף Cd **ברמת השורש**, לצד Serilog ו־Factory, ולא בתוך Factory:

```json
"Cd": {
  "ProjectId": 0,
  "Branch": "master",
  "TemplatePath": "sensorgates/senortemplate",
  "TargetRoot": "sensorgates",
  "TemplateName": "SenorTemplate"
}
```

זכור פסיק בין סעיפים. אפשר להעתיק את הסעיף גם מ־configuration/Cd.fragment.json.

| שדה | מה להגדיר בפועל |
| --- | --- |
| ProjectId | ה־ID המספרי של פרויקט CD הקיים; איני יודע אותו |
| Branch | הענף הקיים שאליו רוצים לדחוף; master הוא רק ברירת מחדל |
| TemplatePath | נתיב התבנית בתוך הריפו |
| TargetRoot | התיקייה שמתחתיה נוצרת תיקיית כל סנסור |
| TemplateName | השם הקנוני בתוך הקבצים, באותיות גדולות/קטנות כמו SenorTemplate |

ProjectId=0 יגרום לכשל בהפעלה, בכוונה, כדי שלא תבוצע יצירה עם הגדרה חסרה. השאר Token תחת Factory כפי שהיה. לא מוסיפים token נוסף ל־CD; משתמשים באותו אימות, והוא צריך לאפשר גם push לענף CD.

אם OpenShift מנהל את הערכים באמצעות environment variables, הוסף ל־Deployment/ConfigMap שלך:

```text
Cd__ProjectId
Cd__Branch
Cd__TemplatePath
Cd__TargetRoot
Cd__TemplateName
```

מפתח API של ComponentFactory נשאר Factory__ApiKey, וטוקן GitLab נשאר Factory__Token. אין שינוי בקונבנציה שלהם.

## שלב 11 — הכן את תבנית CD בריפו הקיים

בתוך ריפו CD עצמו צור את תיקיית `sensorgates/senortemplate` לפי הדוגמה הקיימת. בתוך agent ובתוך poller צריכים להימצא אותם שישה קבצים:

```text
Chart.yaml
values.yaml
develop-values.yaml
integration-values.yaml
production-values.yaml
secondary-values.yaml
```

החלף בתבנית את שם הסנסור הקודם, למשל Adir/adir, בשם התבנית הקנוני ובגרסאות האותיות שלו:

| בתבנית | בסנסור Bravo |
| --- | --- |
| SenorTemplate | Bravo |
| senortemplate | bravo |
| SENORTEMPLATE | BRAVO |

שם התבנית נשמר כפי שביקשת, senortemplate. אם תחליט לכתוב sensortemplate במקום, שנה גם את TemplatePath וגם את TemplateName ואת התוכן, בצורה עקבית.

הקפד על השמות ב־Chart.name, applicationName, image.repository, route.host, labels, annotations, configmaps, queues ושדות שם בתוך JSON. משאבים משותפים כגון sensorgates-agent-config, שמות PVC ו־imagePullSecrets יישארו כפי שהם אם אין בהם את שם התבנית.

הגדר את ה־tag הראשוני בתבנית:

```yaml
generic-chart:
  deployment:
    image:
      tag: 1.0.0
```

אפשר לשים אותו ב־values.yaml המשותף; אם קובץ סביבה מגדיר tag משלו, גם שם הוא צריך להיות 1.0.0. **אין להשאיר override עם tag של Adir.** הקוד מעתיק את הערך ואינו מתקן tag שגוי בתבנית. זו הדרך שנבחרה כדי שלא ליצור מנגנון YAML/גרסאות נוסף עבור סנסור חדש.

commit ו־push של התבנית הזאת צריכים להתבצע לענף CD שהוגדר, לפני הפעלת השירות ליצירת סנסור. תוכן קובצי ה־YAML המלאים לא צורף כאן, כי הוא נקרא מהריפו שלך ולא נבנה מחדש מהתמונות.

## שלב 12 — הגדר את רמת הלוגים

בתמונה שלך MinimumLevel.Default מוגדר Information. ברמה הזאת יופיעו שלבי ה־CD המרכזיים, ניסיונות הפרסום, אזהרות ושגיאות.

לפרטים של כל קובץ ושל כל פעולת Git, מזג לתוך `Serilog.MinimumLevel.Override` הקיים:

```json
"ComponentFactory.Infrastructure": "Debug"
```

לדוגמה, **בתוך** אובייקט Override הקיים:

```json
"Override": {
  "Microsoft": "Warning",
  "Microsoft.Hosting.Lifetime": "Warning",
  "Elastic.Apm": "Error",
  "ComponentFactory.Infrastructure": "Debug"
}
```

שמור את יתר overrides שלך. סעיפי Console.Enabled ו־Logstash שבתמונה שלך שייכים לאתחול הלוגים שלך; אין כאן החלפה שלהם או הוספת sink נוסף. override זה יעבוד כאשר האתחול הקיים שלך טוען MinimumLevel מהקונפיגורציה, כפי שה־Default הנוכחי אמור להיטען.

ה־Information לא תלוי ב־Debug override. על שגיאות CdProvisioner נרשמים גם `RootCause` וגם `Details` כחלק מהודעת הלוג, בנוסף להעברת exception ל־ILogger. לכן המידע נשמר גם אם תבנית פלט קיימת אינה מדפיסה את שדה Exception בנפרד.

נוסף BeginScope עם Sensor, CdProjectId ו־CdBranch. הצגת Scope תלויה ב־provider/formatter הקיים; הלוגים המרכזיים כוללים את שם הסנסור במפורש ואינם מסתמכים רק על scope.

## שלב 13 — עדכן בדיקות ובנאים ידניים

רישומי DI אינם דורשים ארגומנטים ידניים חדשים. אבל אם יש אצלך `new` במחלקות בדיקה או בתשתית ידנית, עדכן:

```csharp
new GitCommandRunner(options, NullLogger<GitCommandRunner>.Instance);

new GitRepository(
    runner,
    new RepositoryUrlValidator(options),
    NullLogger<GitRepository>.Instance);
```

ל־TemplateCustomizer הוסף NullLogger בהתאם לבנאי הקיים שלך. בגרסת reference עם runner:

```csharp
new TemplateCustomizer(
    scanner,
    rewriter,
    options,
    runner,
    NullLogger<TemplateCustomizer>.Instance);
```

ה־using לבדיקות:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
```

גם בנאי ComponentGenerator צריך עכשיו ICdProvisioner. בבדיקת ה־flow אפשר להשתמש ב־fake שמממש אותו, כפי שמופיע ב־tests/GenerationChecks.cs.

מocks שמממשים IGitRepository ו־ITemplateCustomizer צריכים לממש גם את המתודות החדשות מהשלבים הקודמים.

בתיקיית tests המצורפת יש:

- CdProvisioningChecks.cs חדש, עם ריפו Git מקומי וללא גישה ל־GitLab.
- GenerationChecks.cs מעודכן, שבודק שה־CD מגיע אחרי push ושכשל CD שומר את כתובת הסנסור וה־InnerException.
- GitRepositoryChecks.cs מעודכן, כולל בדיקה שפלט כשל של Git מגיע ללוג ולחריגה בלי Token ו־ApiKey שהוגדרו.
- TemplateCustomizationChecks.cs מעודכן לבנאי logger.
- Program.cs של בדיקות הקונסול, עם קריאה ל־CdProvisioningChecks.

אל תעתיק את Program.cs של הבדיקות מעל Program.cs של ה־API; הוא שייך לתיקיית tests בלבד.

## שלב 14 — בנה ובדוק

בסביבת Linux עם .NET 10 SDK ו־Git, מתוך תיקיית הפרויקט:

```bash
dotnet build ComponentFactory.csproj
dotnet run --project tests/FactoryChecks.csproj
```

ב־Visual Studio אפשר לבצע build על Windows. בדיקות Git וה־workspace תואמות ל־runtime Linux של השירות, וחלקן מדלגות על הרצת Git ב־Windows; להרצתן המלאה השתמש בסביבת Linux שכבר משמשת אותך.

אין .NET SDK בסביבה שבה הוכן האוסף, לכן קומפילציה ובדיקות C# לא הורצו כאן. בדיקת רצף Git מקומית ובדיקת התאמת patch לבסיס שלו כן עברו.

אם יש שגיאת בנאי, חפש את כל הקריאות `new GitCommandRunner`, `new GitRepository`, `new TemplateCustomizer` ו־`new ComponentGenerator` ועדכן לפי השלב הקודם. אם יש שגיאת ממשק, עדכן את ה־fakes/mocks.

## שלב 15 — הרצה ראשונה ב־OpenShift

פרוס את גרסת השירות שלך כפי שאתה עושה כיום. אין כאן שינוי ב־Dockerfile, ב־CA שהתקנת, ב־Swagger או בפרופיל ההפעלה.

שלח דרך ה־API שם סנסור חדש שאינו קיים עדיין. עבור Bravo, ודא:

1. פרויקט הקוד נוצר ב־namespace/subgroup שהגדרת קודם וה־push שלו הצליח.
2. בריפו CD הקיים נוצרה `sensorgates/bravo` עם agent ו־poller.
3. בכל תפקיד יש Chart.yaml וחמשת קובצי values.
4. אין שם של הסנסור הקודם בקבצים החדשים; השמות של Bravo מופיעים במקומות המתאימים.
5. ה־tag האפקטיבי הראשוני הוא 1.0.0 גם אחרי קובצי override של הסביבות.
6. תבנית senortemplate ושאר תיקיות הסנסורים נשארו ללא שינוי.
7. התשובה חזרה רק אחרי סיום push של CD.

## מה אמור להופיע בלוגים

ברמת Information, בריצה מוצלחת, תחפש לפי שם הסנסור:

```text
Sensor Bravo was created and pushed ...; starting CD provisioning
Preparing CD files for Bravo in project ...
Reading existing CD project ... for Bravo
Cloning CD project ..., branch ..., for Bravo
CD clone completed for Bravo ...
Copying CD template sensorgates/senortemplate to sensorgates/bravo for Bravo
Copied 12 CD files for Bravo; customizing template name SenorTemplate
CD customization completed for Bravo ...
Creating CD commit for Bravo; staging only sensorgates/bravo
CD commit completed for Bravo
Publishing CD files for Bravo ...; attempt 1/3
CD files published for Bravo ... in ... ms
Sensor Bravo and its CD files were published successfully; returning response
```

אם בתבנית יש קבצים נוספים, מספר הקבצים בלוג עשוי להיות גדול מ־12. בדיקת החובה היא שכל 12 הקבצים הידועים קיימים.

ברמת Debug תראה גם את שמות הקבצים היחסיים שהועתקו, פרטי הסריקה, ושם פעולת Git וזמן הריצה שלה. תוכן קובצי YAML, משתני הסביבה והטוקן אינם נרשמים ביוזמת הקוד.

## איפה לחפש במקרה של כשל

| לוג/Stage אחרון | מה לבדוק |
| --- | --- |
| Reading existing CD project | ProjectId, גישה ל־GitLab ושרשרת החריגה של HTTP |
| Cloning CD project | branch, הרשאות clone ו־GitError של פעולת clone |
| Copying CD template / cd-prepare | TemplatePath, קיום 12 הקבצים, תיקיית יעד קיימת או symlink |
| customizing template name | שם התבנית, התנגשות בשמות וקוד TemplateCustomizer |
| cd-commit | GitError של add או commit והנתיב שהועבר ל־staging |
| cd-push | GitError של push, הרשאות לענף, fetch/rebase או merge conflict |
| RootCause / Details | הסיבה הפנימית וה־stack trace שנשמרו |

אם push נדחה עקב שינוי מקביל, יופיעו Warning, fetch, rebase וניסיון נוסף. אין force-push ואין פתרון אוטומטי ל־conflict. עד שלושה ניסיונות פרסום.

אם CD נכשל אחרי יצירת הסנסור, תתקבל שגיאת GenerationIncomplete עם כתובת הסנסור שנשמר. **אל תשלח שוב אותה בקשת יצירה מתוך הנחה שלא נוצר כלום**: הסנסור כבר קיים. קרא את לוג CD והשלם או תקן את השלב שנכשל.

אם pipeline של הסנסור מנסה לכתוב לתיקיית CD מיד אחרי ה־push הראשון, הוא עשוי להתחיל לפני שהתיקייה פורסמה. לפי ה־flow שביקשת, pipeline כזה צריך להמתין או לנסות שוב. הקוד הזה אינו ממתין לסיום pipeline ואינו משנה את pipeline שלך.

## אם אתה משתמש ב־patch

ה־patch מיועד לבסיס שתואר בתחילת המדריך, ולא לגרסה העדכנית ששינית בעצמך. מתוך שורש הפרויקט אפשר לבדוק התאמה:

```bash
git apply --check /path/to/changes/existing-files.patch
```

אם הבדיקה מצליחה והשינויים מתאימים לקוד שלך, אפשר להחיל עם git apply. אם לא, שלב ידנית לפי המדריך. אל תשתמש בהחלפה מלאה של reference-existing רק כדי לעקוף חוסר התאמה.

ה־patch משנה שמונה קבצים קיימים: שבעת הקבצים מהתוכנית המקורית ועוד GitCommandRunner בעקבות בקשת הלוגים. ארבעת הקבצים החדשים נמצאים ב־new-files ומועתקים בנפרד.
