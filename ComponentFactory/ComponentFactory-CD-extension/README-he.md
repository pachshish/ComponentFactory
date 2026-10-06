# הרחבת ComponentFactory ליצירת קובצי CD

## מה מצורף

זהו אוסף קבצים לשילוב בפרויקט הקיים, ולא פרויקט חדש.

- `new-files/`: ארבעת קובצי ה־C# החדשים. העתק אותם לשורש פרויקט ComponentFactory, תוך שמירת הנתיבים.
- `changes/existing-files.patch`: השינויים בשמונה הקבצים הקיימים, ללא ארבעת הקבצים החדשים. נוספה גם הרחבת הלוגים ב־GitCommandRunner.
- `reference-existing/`: שבעת קובצי ה־C# הקיימים לאחר השינוי, לעיון והשוואה.
- `configuration/Cd.fragment.json`: סעיף Cd בלבד להוספה ל־appsettings.json.
- `tests/`: בדיקת CD חדשה ועדכוני בדיקות התהליך הקיימות.
- `validation/check_git_workflow.py`: בדיקה מקומית של רצף פקודות ה־Git; אינה מריצה C#.
- `VALIDATION.md`: מה נבדק ומה עדיין צריך להריץ.
- `INTEGRATION-STEP-BY-STEP-he.md`: מדריך מפורט לפי קובץ ומתודה, כולל בנאים, לוגים, הגדרות ובדיקת ההתקנה.

**בסיס השינויים:** העותק שנשלח קודם, לאחר תיקון ה־submodules שסופק בצ'אט. הגרסה שאתה תיקנת בעצמך אינה נמצאת אצלי. אל תחליף אוטומטית את הקבצים הקיימים בקובצי reference: שלב את התוספות באמצעות השוואה, ושמור את תיקוני ה־submodules, האימות והלוגים שלך. אין שינוי ב־GitLabClient, ב־FactoryExceptionHandler, ב־Program.cs או ב־Dockerfile.

## סדר הפעולות

קבלת שם → הכנת קוד הסנסור → יצירת פרויקט הסנסור → push של הסנסור → שכפול ריפו CD קיים → העתקת התבנית והתאמתה → commit ו־push של CD → תשובה.

לא נוצר פרויקט CD חדש. ההיסטוריה של ריפו CD נשמרת. התשובה הקיימת נשארת כפי שהיא, ומוחזרת רק לאחר פרסום CD בהצלחה.

## שילוב בקוד הקיים

### 1. העתק את ארבעת הקבצים החדשים

```text
Application/Abstractions/ICdProvisioner.cs
Infrastructure/Cd/CdProvisioner.cs
Configuration/CdOptions.cs
Configuration/CdOptionsValidator.cs
```

### 2. הרחב את IGitRepository ואת GitRepository

הוסף לחוזה ולמימוש:

```csharp
Task CloneAsync(GitLabProject project, IWorkspace workspace, string branch, CancellationToken cancellationToken);
Task CommitAsync(IWorkspace workspace, string message, string relativePath, CancellationToken cancellationToken);
Task PushAsync(IWorkspace workspace, GitLabProject target, string branch, CancellationToken cancellationToken);
Task RefreshBranchAsync(IWorkspace workspace, string branch, CancellationToken cancellationToken);
```

- `CloneTemplateAsync` הקיים יעביר ל־`CloneAsync` את `RepositoryDefaults.Branch`. שמור את דגלי השכפול הרקורסיבי שעובדים אצלך.
- `CommitAsync` מרכז את הגדרת המחבר, `git add --all -- <relativePath>` ואת ה־commit. פעולת ה־initial commit של הסנסור קוראת אליו עם `.`; שירות CD קורא אליו רק עם נתיב תיקיית הסנסור החדשה.
- **שמור את דרך יצירת ה־initial commit שעובדת אצלך**, כולל טיפול ב־submodules. אין צורך להחליף אותה; רק הוצא את פעולות author/add/commit המשותפות למתודה הזאת.
- `PushAsync` הישן מעביר ל־overload החדש את ענף הסנסור. ה־overload החדש מגדיר `remote.origin.url` ודוחף `HEAD:refs/heads/<branch>`, ללא force.
- `RefreshBranchAsync` עושה fetch ואז rebase על `FETCH_HEAD`. הוא מסיר את מגבלת shallow רק אם `.git/shallow` עדיין קיים. כך גם ניסיון שני של refresh עובד.
- אפשר להשאיר כל מתודה נוספת שכבר יש אצלך בחוזה, כולל פעולות שלא מופיעות בגרסת reference.
- למימוש GitRepository נוסף `ILogger<GitRepository>` בבנאי ושדה `_logger`. אם כבר יש לך logger כזה, השתמש בו. עדכן גם קריאות `new GitRepository` בבדיקות.

### 3. הרחב את ITemplateCustomizer ואת TemplateCustomizer

הוסף overload:

```csharp
Task CustomizeAsync(string rootPath, ComponentName name, string templateName, CancellationToken cancellationToken);
```

המתודה הישנה קוראת למתודה החדשה עם שם התבנית שכבר נטען מ־FactoryOptions. בתוך המתודה החדשה יוצרים `NameReplacement` באמצעות `templateName` שהועבר, ושומרים את מנגנוני הסריקה, ההחלפה וההחרגה של submodules שכבר עובדים אצלך.

אין צורך להוסיף GitCommandRunner לבנאי שלך אם הוא לא קיים שם כרגע. התוספת להחלפת השמות היא ה־overload והעברת שם התבנית. שירות CD מעביר את תיקיית הסנסור שהועתקה, ולא את שורש הריפו כולו.

לצורך הלוגים הוסף `ILogger<TemplateCustomizer>` ושדה `_logger`, אם אינם קיימים אצלך. בגרסת reference עם GitCommandRunner הבנאי קיבל logger נוסף; זה לא מחייב אותך להוסיף runner לבנאי שלך.

### 3א. הוסף אבחון ב־GitCommandRunner

ל־GitCommandRunner נוסף `ILogger<GitCommandRunner>` בבנאי. שיטת ההרצה נשארה מרוכזת במקום אחד. נוספו זמן ריצה, לוג ביטול, וקריאת stderr לאחר סיום התהליך. פלט הכשל עובר הסתרת Token ו־ApiKey, credentials ב־URL וכותרות Authorization לפני כניסתו ללוג ול־InnerException. אין רישום של מלוא שורת הפקודה או משתני הסביבה.

שלב את `RunAsync`, את `SanitizeDiagnostic` ואת גרסת `CommandFailure` מקובץ reference, תוך שמירת הגדרות האימות שלך. פרטים מדויקים במדריך השלבים. עדכן קריאות `new GitCommandRunner` בבדיקות.

### 4. חבר את ComponentGenerator

הוסף `ICdProvisioner cdProvisioner` לבנאי ואת השדה:

```csharp
private readonly ICdProvisioner _cdProvisioner = cdProvisioner;
```

ב־`GenerateAsync`, מיד אחרי ה־push הקיים ולפני בניית התשובה:

```csharp
await PushToCreatedProjectAsync(workspace, project, operationToken);
await ProvisionCdAsync(name, project, operationToken);
```

הוסף את `ProvisionCdAsync` מקובץ reference. היא קוראת לשירות CD ועוטפת כשל ב־`GenerationIncomplete`, תוך שמירת כתובת הסנסור שכבר פורסם, `Stage` של CD וכל שרשרת `InnerException`.

עדכן גם יצירות ידניות של ComponentGenerator ובדיקות שיש בהן בנאי ישן.

### 5. רשום את השירות ואת ההגדרות

ב־ServiceCollectionExtensions הוסף:

```csharp
using ComponentFactory.Infrastructure.Cd;
```

ב־`AddConfiguration`:

```csharp
services.AddSingleton<IValidateOptions<CdOptions>, CdOptionsValidator>();
services.AddOptions<CdOptions>()
    .BindConfiguration(CdOptions.SectionName)
    .ValidateOnStart();
```

ב־`AddGenerationServices`:

```csharp
services.AddTransient<ICdProvisioner, CdProvisioner>();
```

### 6. הוסף את ההגדרות

מזג את סעיף `Cd` מהקובץ `configuration/Cd.fragment.json` לתוך appsettings.json שלך. אל תחליף את יתר ההגדרות, כולל Serilog ו־Factory.

```json
"Cd": {
  "ProjectId": 0,
  "Branch": "master",
  "TemplatePath": "sensorgates/senortemplate",
  "TargetRoot": "sensorgates",
  "TemplateName": "SenorTemplate"
}
```

`ProjectId: 0` הוא מקום למילוי, ונדחה בזמן עליית השירות. מלא את ה־ID של **פרויקט CD הקיים**. אין לי את ה־ID הזה.

`master` הוא ברירת מחדל, ולא קביעה שזה ענף CD שלך. הגדר את הענף האמיתי.

הנתיבים הם בתוך הריפו, ולא שם קבוצת GitLab. לפי התמונות השתמשנו ב־`sensorgates` ברבים. אם הנתיב האמיתי אצלך אחר, שנה את שתי הגדרות הנתיבים.

אפשר להגדיר ב־OpenShift באמצעות:

```text
Cd__ProjectId
Cd__Branch
Cd__TemplatePath
Cd__TargetRoot
Cd__TemplateName
```

נעשה שימוש ב־Factory:Token ובאימות הקיים. אותו token צריך הרשאת קריאה ו־push לענף CD שהוגדר.

## הכנת תבנית CD

בתוך ריפו CD עצמו צור:

```text
sensorgates/senortemplate/agent/
sensorgates/senortemplate/poller/
```

בכל תפקיד צריכים להימצא:

```text
Chart.yaml
values.yaml
develop-values.yaml
integration-values.yaml
production-values.yaml
secondary-values.yaml
```

הקוד בודק את קיום כל 12 הקבצים לפני ההעתקה. הקבצים האמיתיים נלקחים מהריפו שלך; לא נוצר כאן YAML משוער מהתמונות.

השתמש בשם עקבי בתבנית:

| מופע בתבנית | עבור סנסור Bravo |
| --- | --- |
| SenorTemplate | Bravo |
| senortemplate | bravo |
| SENORTEMPLATE | BRAVO |

שמות כגון `senortemplate-agent`, repository של image, כתובות route, labels, configmaps, queues ושדות השם בתוך JSON יותאמו באמצעות מנגנון ההחלפה הקיים. שמות משאבים משותפים שאין בהם את שם התבנית יישארו כפי שהם.

**image tag:** לפי התוכנית שאושרה, הגדר מראש `tag: 1.0.0` בתבנית, תחת `generic-chart.deployment.image`, בכל values שמגדיר tag. אפשר להגדירו ב־values.yaml המשותף ולוודא שאין override לגרסה אחרת בארבעת קובצי הסביבה. הקוד מעתיק את הערך ואינו מחשב או משנה גרסה. הוא גם אינו מנתח YAML כדי לתקן tag שגוי בתבנית: ערך קבוע נכון בתבנית הוא תנאי השימוש.

ה־Chart.yaml יכול להמשיך להפנות ל־generic-chart הקיים. אין צורך לייצר מחדש Deployment/Service/Route בתוך שירות ComponentFactory.

## שגיאות ופרסום מקביל

- תיקיית סנסור שכבר קיימת ב־CD נדחית ולא נדרסת.
- כשל בהכנה: `cd-prepare`.
- כשל ב־commit: `cd-commit`.
- כשל בפרסום: `cd-push`.
- כשל CD אחרי פרסום הסנסור מחזיר שגיאת יצירה חלקית עם כתובת הסנסור. לא מוחקים את הסנסור שכבר נוצר ולא מחזירים תשובת הצלחה.
- עד שלושה ניסיונות push. אחרי דחייה עושים fetch/rebase לפני ניסיון נוסף. מאחר שמריץ Git הקיים אינו מסווג stderr, לא מניחים שכל כשל הוא בהכרח תחרות: כשל אימות, רשת או rebase ידווח אם לא נפתר. אין force-push ואין ניסיון אוטומטי לפתור merge conflict.
- משתמשים ב־timeout וב־CancellationToken של הפעולה כולה, כולל CD. ה־workspace הנוסף מנוקה גם אחרי כשל.
- קריאה חוזרת לאותה בקשת יצירה אינה מסלול תיקון: פרויקט הסנסור כבר קיים. אחרי כשל חלקי מתקנים או משלימים את שלב CD עבור הפרויקט שנשמר.
- אם pipeline הסנסור מנסה לעדכן CD מיד אחרי ה־push הראשון, הוא יכול להתחיל לפני סיום יצירת תיקיית CD. זה נובע מה־flow שביקשת. pipeline כזה צריך להמתין לתיקייה או לנסות שוב; אין כאן המתנה לסיום pipeline או שינוי בו.

## בדיקות ושילוב ה־patch

אם הקוד שלך תואם לבסיס המצורף, מתוך שורש פרויקט ComponentFactory אפשר לבדוק התאמה של ה־patch:

```bash
git apply --check /path/to/changes/existing-files.patch
```

רק אם הבדיקה עוברת והשינויים מתאימים לגרסה שלך, אפשר להחיל. אם היא נכשלת, שלב ידנית לפי ההוראות; אל תשתמש ב־force או בהחלפה מלאה של קבצים מתוקנים.

```bash
git apply /path/to/changes/existing-files.patch
```

ה־patch אינו מוסיף את ארבעת הקבצים החדשים: העתק אותם בנפרד מ־new-files.

בתיקיית tests יש `CdProvisioningChecks.cs` חדש, גרסה מעודכנת של GenerationChecks, ושורת הקריאה ב־Program של בדיקות הקונסול הקיימות. אלו משתמשים ב־Assert וב־TestDirectory שכבר קיימים בפרויקט הבדיקות שנשלח קודם. אם יש אצלך mocks אחרים ל־IGitRepository או ITemplateCustomizer, עדכן גם אותם למתודות הנוספות.

לאחר השילוב, בסביבת Linux עם .NET 10 SDK ו־Git:

```bash
dotnet build ComponentFactory.csproj
dotnet run --project tests/FactoryChecks.csproj
```

הבדיקות החדשות משתמשות בריפו Git מקומי בלבד; הן לא יוצרות פרויקטים ב־GitLab ולא ניגשות לריפו CD שלך.
