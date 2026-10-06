using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace ByteBridge.Localization;

/*
 * Simple localization manager for Arabic and English.
 *
 * Loads strings from a dictionary based on the current
 * culture. Falls back to English if a translation is missing.
 */
public static class Strings
{
    private static readonly Dictionary<string, Dictionary<string, string>> Translations = new()
    {
        ["en"] = new()
        {
            // Window
            ["AppTitle"] = "ByteBridge",
            ["AddData"] = "+ Add Data",
            ["Done"] = "Done",
            ["Settings"] = "Settings",

            // Menu bar
            ["MenuFile"] = "File",
            ["MenuFileNew"] = "New Database…",
            ["MenuFileExit"] = "Exit",
            ["MenuEdit"] = "Edit",
            ["MenuEditEdit"] = "Edit Selected",
            ["MenuEditDelete"] = "Delete Selected",
            ["MenuWebServer"] = "Web Server",
            ["MenuCloudflareTunnel"] = "Cloudflare Tunnel",
            ["MenuConnect"] = "Connect to ByteBalance",
            ["MenuOptions"] = "Options",
            ["MenuHelp"] = "Help",
            ["MenuHelpGuide"] = "User Guide",
            ["MenuHelpWebsite"] = "Visit ByteBalanceTech.com",
            ["MenuHelpAbout"] = "About ByteBridge",
            ["MenuHelpOpenLogs"] = "Open Log Folder",

            // Add/Edit Database wizard
            ["WizardAddTitle"] = "Add Database",
            ["WizardEditTitle"] = "Edit Database",
            ["WizardStep1Title"] = "Connection Name",
            ["WizardStep1Hint"] = "Give this connection a name you'll recognize.",
            ["WizardStep2Title"] = "Server Details",
            ["WizardStep3Title"] = "Credentials",
            ["WizardStep3Hint"] = "This is the database password.",
            ["WizardStep4Title"] = "Test & Finish",
            ["WizardStep4Hint"] = "Run a test before saving, so a typo doesn't turn into a connection that never comes online.",
            ["WizardConnectionName"] = "Connection Name",
            ["WizardServer"] = "Server",
            ["WizardPort"] = "Port",
            ["WizardUsername"] = "Username",
            ["WizardPassword"] = "Password",
            ["WizardDatabase"] = "Database",
            ["WizardDatabaseHint"] = "Enter the database alias or database path.",
            ["WizardEngine"] = "Database type",
            ["WizardDatabaseName"] = "Database name",
            ["WizardDatabaseNameHint"] = "The name of the database on the server, not a file path.",
            ["WizardBack"] = "Back",
            ["WizardNext"] = "Next",
            ["WizardTestConnection"] = "Test Connection",
            ["WizardTestSucceeded"] = "Connection succeeded.",
            ["WizardEngineUnsupported"] =
                "This connection names a database engine this version of " +
                "ByteBridge cannot serve. Choose Firebird or PostgreSQL " +
                "before saving.",
            ["EngineUnsupported"] = "Engine not supported",
            ["WizardTestFailed"] = "Connection failed.\n\n{0}",
            ["WizardFinish"] = "Finish",
            ["WizardSave"] = "Save",

            // Web Server / Cloudflare Tunnel / About dialogs
            ["WebServerWindowTitle"] = "Web Server",
            ["CloudflareTunnelWindowTitle"] = "Cloudflare Tunnel",

            // Connect to ByteBalance dialog
            ["ConnectWindowTitle"] = "Connect to ByteBalance",
            ["ConnectIntro"] = "Gives this machine a private tunnel to ByteBalance so your storefront can read from it. ByteBalance approves each request, and only the email below will be let through to the tunnel.",
            ["ConnectEmail"] = "Owner email",
            ["ConnectEmailRequired"] = "Please enter the email address that should be the only one allowed through to this machine's tunnel.",
            ["ConnectAction"] = "Connect",
            ["ConnectCheck"] = "Check approval",
            ["ConnectSyncKey"] = "Send API key again",
            ["ConnectDisconnect"] = "Disconnect",
            ["ConnectDisconnectConfirm"] = "Remove the tunnel connector from this machine and forget the enrolment?\n\nByteBalance keeps the device until its administrator removes it, and this machine cannot connect again until then.",
            ["ConnectStateNone"] = "Not connected to ByteBalance.",
            ["ConnectStateRequested"] = "Waiting for ByteBalance to approve this machine.",
            ["ConnectStateConnected"] = "Connected as {0}",
            ["AboutTitle"] = "About ByteBridge",
            ["AboutVersion"] = "Version {0}",
            ["AboutDescription"] = "HTTP gateway for your databases.",
            ["AboutMadeBy"] = "Developed by",
            ["AboutOpenLogs"] = "Open Log Folder",
            ["AboutClose"] = "Close",

            // Traffic
            ["RequestCount"] = "{0} requests",
            ["RequestCountUnknown"] = "—",

            // Gateway
            ["GatewayApi"] = "Gateway API",
            ["Port"] = "Port",
            ["CopyApiKey"] = "Copy API Key",
            ["NewKey"] = "New Key",
            ["StartService"] = "Start Service",
            ["TurnOn"] = "Turn On",
            ["TurnOff"] = "Turn Off",
            ["Answering"] = "● Answering — {0}",
            ["TurnedOff"] = "● Turned off",
            ["Starting"] = "● Starting, or unable to bind",
            ["NotRunning"] = "● Not running",
            ["ServiceRunning"] = "Service: running",
            ["ServiceStopped"] = "Service: stopped",
            ["ServicePending"] = "Service: starting or stopping",
            ["ServiceNotInstalled"] = "Service: not installed — reinstall ByteBridge to add it",
            ["TunnelHint"] = "Point the tunnel here:  cloudflared tunnel --url {0}",
            ["TurnedOffHint"] = "The gateway is set not to listen. A tunnel pointed at this machine will return 502 until it is turned on.",
            ["StartingHint"] = "The service is running but nothing answers on {0}. Give it a few seconds; if it stays this way the port is in use or the reservation was refused. See Event Viewer, Application, source ByteBridge.",
            ["NotRunningHint"] = "The service that hosts the gateway is not running, so a tunnel pointed at this machine will return 502.",

            // Connections
            ["NoDatabases"] = "No databases configured.\n\nUse File → New Database… to create a connection.",
            ["Online"] = "● Online",
            ["Offline"] = "● Offline",
            ["OfflineButton"] = "Offline",
            ["OnlineButton"] = "Online",
            ["Edit"] = "Edit",
            ["Delete"] = "Delete",

            // Cloudflare OAuth
            ["CloudflareLogin"] = "Cloudflare Login",
            ["NotConfigured"] = "Not configured",
            ["Enabled"] = "Enabled",
            ["TeamDomain"] = "Team Domain",
            ["Audience"] = "Audience",
            ["PublicHostname"] = "Public Hostname",
            ["Enable"] = "Enable",
            ["Disable"] = "Disable",

            // Dialogs
            ["CopyKeyTitle"] = "API Key",
            ["CopyKeyMessage"] = "API key copied.\n\nSend it on every request as the X-API-Key header.",
            ["CopyKeyError"] = "The key could not be copied.\n\n{0}",
            ["NewKeyTitle"] = "New API Key",
            ["NewKeyConfirm"] = "Generate a new API key?\n\nEvery client still using the current key will be rejected until it is updated.",
            ["NewKeyMessage"] = "A new API key was generated.\n\nUse Copy API Key to put it on the clipboard.",
            ["DeleteTitle"] = "Delete Database",
            ["DeleteConfirm"] = "Delete \"{0}\"?\n\nThis connection will be permanently removed.",
            ["TurnOnlineTitle"] = "Turn Online",
            ["TurnOnlineConfirm"] = "Turn \"{0}\" online?\n\nByteBridge will test the database connection first.",
            ["TurnOfflineTitle"] = "Turn Offline",
            ["TurnOfflineConfirm"] = "Turn \"{0}\" offline?",
            ["ConnectionFailed"] = "Connection Failed",
            ["ConnectionFailedMessage"] = "The database connection failed.\n\nThe connection will remain Offline.",
            ["ConnectionFailedError"] = "The database connection failed.\n\n{0}",
            ["InvalidPort"] = "Please enter a valid port between 1 and 65535.",
            ["ServiceError"] = "The ByteBridge service could not be started.\n\n{0}",
            ["ServiceTitle"] = "Service",
            ["StopService"] = "Stop Service",
            ["StopServiceConfirm"] = "Stopping the service takes the gateway offline. A tunnel pointed at this machine will return 502 until it is started again.\n\nStop it?",
            ["ServiceStopError"] = "The ByteBridge service could not be stopped.\n\n{0}",
            ["ServiceStoppedPrompt"] = "The ByteBridge service is not running, so the gateway is offline and a tunnel pointed at this machine will return 502.\n\nStart it now?",
            ["ServiceNotInstalledMessage"] = "The ByteBridge service is not installed on this computer, so the gateway cannot run. Reinstall ByteBridge to add it.",

            // OAuth dialogs
            ["OAuthDisabled"] = "Cloudflare OAuth login has been disabled.\n\nUsers will need to use the API key to authenticate.",
            ["OAuthEnabled"] = "Cloudflare OAuth login has been enabled.\n\nMake sure you have configured Cloudflare Access\nwith an identity provider and created an application\nfor your gateway hostname.",
            ["OAuthTeamDomainRequired"] = "Please enter your Cloudflare Access team domain.\n\nExample: my-team.cloudflareaccess.com",
            ["OAuthAudienceRequired"] = "Please enter the Access application audience tag.\n\nFind it in Zero Trust → Access → Applications → Settings.",
            ["OAuthPublicHostnameRequired"] = "Please enter the public hostname your Cloudflare Tunnel exposes.\n\nExample: api.yourcompany.com\n\nThis is where Cloudflare Access sends visitors back after they sign in — without it, login cannot complete.",

            // Close dialog
            ["CloseTitle"] = "ByteBridge",
            ["CloseMessage"] = "What would you like to do?",
            ["MinimizeToTray"] = "Minimize to Tray",
            ["ExitApp"] = "Exit",
            ["Cancel"] = "Cancel",

            // Tray icon
            ["TrayOpen"] = "Open ByteBridge",

            // Settings
            ["AutoStart"] = "Start with Windows",
            ["AutoStartHint"] = "ByteBridge will start automatically when you log in.",
            ["LockoutAttempts"] = "Lock out after wrong keys",
            ["LockoutHint"] = "How many wrong API keys one caller may send within {0} seconds before being refused for a while. 0 turns it off.",
            ["LockoutMinutes"] = "Lock out for (minutes)",
            ["InvalidLockout"] = "Attempts must be a number from 0 to 10000, and the lock-out length from 1 to 1440 minutes.",
            ["DontAskAgain"] = "Don't ask again",
            ["AskBeforeClosing"] = "Ask before closing",
            ["AskBeforeClosingHint"] = "Choose between minimizing to the tray and exiting each time the window is closed.",
            ["AllowWriting"] = "Allow writing",
            ["AllowWritingHint"] = "Off: the gateway only reads. On: it also runs statements that change data. Needs administrator rights.",
            ["AllowWritingConfirm"] = "Turn writing on?\n\nAnyone who holds the API key, or is signed in through ByteBridge's Cloudflare login, will be able to change and delete data, and to change the structure of your databases, through the gateway.\n\nOnly turn this on if you need it, and turn it off again afterwards.",
            ["AllowWritingNeedsAdmin"] = "Only an administrator can change this.",
            ["AllowWritingError"] = "Could not change the setting: {0}",
            ["Language"] = "Language",
            ["LanguageHint"] = "Select the display language",
            ["English"] = "English",
            ["Arabic"] = "العربية",
        },

        ["ar"] = new()
        {
            // Window
            ["AppTitle"] = "ByteBridge",
            ["AddData"] = "+ إضافة بيانات",
            ["Done"] = "تم",
            ["Settings"] = "الإعدادات",

            // Menu bar
            ["MenuFile"] = "ملف",
            ["MenuFileNew"] = "قاعدة بيانات جديدة...",
            ["MenuFileExit"] = "خروج",
            ["MenuEdit"] = "تعديل",
            ["MenuEditEdit"] = "تعديل المحدد",
            ["MenuEditDelete"] = "حذف المحدد",
            ["MenuWebServer"] = "سيرفر ويب",
            ["MenuCloudflareTunnel"] = "نفق Cloudflare",
            ["MenuConnect"] = "الاتصال بـ ByteBalance",
            ["MenuOptions"] = "خيارات",
            ["MenuHelp"] = "تعليمات",
            ["MenuHelpGuide"] = "دليل الاستخدام",
            ["MenuHelpWebsite"] = "زيارة موقع ByteBalanceTech.com",
            ["MenuHelpAbout"] = "حول ByteBridge",
            ["MenuHelpOpenLogs"] = "فتح مجلد السجلات",

            // Add/Edit Database wizard
            ["WizardAddTitle"] = "إضافة قاعدة بيانات",
            ["WizardEditTitle"] = "تعديل قاعدة بيانات",
            ["WizardStep1Title"] = "اسم الاتصال",
            ["WizardStep1Hint"] = "أعطِ هذا الاتصال اسماً تتعرف عليه.",
            ["WizardStep2Title"] = "تفاصيل السيرفر",
            ["WizardStep3Title"] = "بيانات الدخول",
            ["WizardStep3Hint"] = "هذه كلمة سر قاعدة البيانات.",
            ["WizardStep4Title"] = "اختبار وإنهاء",
            ["WizardStep4Hint"] = "قم بالاختبار قبل الحفظ، حتى لا يتحول خطأ إملائي إلى اتصال لن يعمل أبداً.",
            ["WizardConnectionName"] = "اسم الاتصال",
            ["WizardServer"] = "السيرفر",
            ["WizardPort"] = "المنفذ",
            ["WizardUsername"] = "اسم المستخدم",
            ["WizardPassword"] = "كلمة السر",
            ["WizardDatabase"] = "قاعدة البيانات",
            ["WizardDatabaseHint"] = "أدخل اسم قاعدة البيانات المستعار أو مسارها.",
            ["WizardEngine"] = "نوع قاعدة البيانات",
            ["WizardDatabaseName"] = "اسم قاعدة البيانات",
            ["WizardDatabaseNameHint"] = "اسم قاعدة البيانات على السيرفر، وليس مسار ملف.",
            ["WizardBack"] = "السابق",
            ["WizardNext"] = "التالي",
            ["WizardTestConnection"] = "اختبار الاتصال",
            ["WizardTestSucceeded"] = "نجح الاتصال.",
            ["WizardEngineUnsupported"] =
                "هذا الاتصال يشير إلى نوع قاعدة بيانات لا تدعمه هذه " +
                "النسخة من ByteBridge. اختر Firebird أو PostgreSQL قبل الحفظ.",
            ["EngineUnsupported"] = "المحرك غير مدعوم",
            ["WizardTestFailed"] = "فشل الاتصال.\n\n{0}",
            ["WizardFinish"] = "إنهاء",
            ["WizardSave"] = "حفظ",

            // Web Server / Cloudflare Tunnel / About dialogs
            ["WebServerWindowTitle"] = "سيرفر ويب",
            ["CloudflareTunnelWindowTitle"] = "نفق Cloudflare",

            // Connect to ByteBalance dialog
            ["ConnectWindowTitle"] = "الاتصال بـ ByteBalance",
            ["ConnectIntro"] = "يمنح هذا الجهاز نفقاً خاصاً إلى ByteBalance ليتمكن متجرك من القراءة منه. تعتمد ByteBalance كل طلب، ولن يُسمح بالمرور إلى النفق إلا للبريد الموضّح أدناه.",
            ["ConnectEmail"] = "بريد المالك",
            ["ConnectEmailRequired"] = "الرجاء إدخال البريد الإلكتروني الوحيد المسموح له بالمرور إلى نفق هذا الجهاز.",
            ["ConnectAction"] = "اتصال",
            ["ConnectCheck"] = "تحقق من الموافقة",
            ["ConnectSyncKey"] = "إعادة إرسال مفتاح API",
            ["ConnectDisconnect"] = "قطع الاتصال",
            ["ConnectDisconnectConfirm"] = "إزالة موصّل النفق من هذا الجهاز ونسيان التسجيل؟\n\nتحتفظ ByteBalance بالجهاز حتى يزيله مسؤولها، ولا يمكن لهذا الجهاز الاتصال مجدداً قبل ذلك.",
            ["ConnectStateNone"] = "غير متصل بـ ByteBalance.",
            ["ConnectStateRequested"] = "بانتظار موافقة ByteBalance على هذا الجهاز.",
            ["ConnectStateConnected"] = "متصل باسم {0}",
            ["AboutTitle"] = "حول ByteBridge",
            ["AboutVersion"] = "الإصدار {0}",
            ["AboutDescription"] = "بوابة HTTP لقواعد بياناتك.",
            ["AboutMadeBy"] = "تطوير",
            ["AboutOpenLogs"] = "فتح مجلد السجلات",
            ["AboutClose"] = "إغلاق",

            // Traffic
            ["RequestCount"] = "{0} طلب",
            ["RequestCountUnknown"] = "—",

            // Gateway
            ["GatewayApi"] = "واجهة API",
            ["Port"] = "المنفذ",
            ["CopyApiKey"] = "نسخ مفتاح API",
            ["NewKey"] = "مفتاح جديد",
            ["StartService"] = "بدء الخدمة",
            ["TurnOn"] = "تشغيل",
            ["TurnOff"] = "إيقاف",
            ["Answering"] = "● يعمل — {0}",
            ["TurnedOff"] = "● متوقف",
            ["Starting"] = "● قيد التشغيل، أو تعذر الاتصال",
            ["NotRunning"] = "● غير يعمل",
            ["ServiceRunning"] = "الخدمة: تعمل",
            ["ServiceStopped"] = "الخدمة: متوقفة",
            ["ServicePending"] = "الخدمة: قيد التشغيل أو الإيقاف",
            ["ServiceNotInstalled"] = "الخدمة: غير مثبتة — أعد تثبيت ByteBridge لإضافتها",
            ["TunnelHint"] = "وجّه النفق هنا:  cloudflared tunnel --url {0}",
            ["TurnedOffHint"] = "تم إعداد البوابة لعدم الاستماع. سيُرجع النفق الموجّه إلى هذا الجهاز خطأ 502 حتى يتم تشغيله.",
            ["StartingHint"] = "الخدمة تعمل ولكن لا أحد يستجيب على {0}. انتظر بضع ثوانٍ؛ إذا استمر هذا، فالمنفذ مستخدم أو تم رفض الحجز. راجع عارض الأحداث، التطبيق، مصدر ByteBridge.",
            ["NotRunningHint"] = "الخدمة التي تستضيف البوابة غير تعمل، لذا سيُرجع النفق الموجّه إلى هذا الجهاز خطأ 502.",

            // Connections
            ["NoDatabases"] = "لا توجد قواعد بيانات مُعدّة.\n\nاستخدم ملف ← قاعدة بيانات جديدة... لإنشاء اتصال.",
            ["Online"] = "● متصل",
            ["Offline"] = "● غير متصل",
            ["OfflineButton"] = "غير متصل",
            ["OnlineButton"] = "متصل",
            ["Edit"] = "تعديل",
            ["Delete"] = "حذف",

            // Cloudflare OAuth
            ["CloudflareLogin"] = "تسجيل الدخول عبر Cloudflare",
            ["NotConfigured"] = "غير مُعدّ",
            ["Enabled"] = "مُفعّل",
            ["TeamDomain"] = "نطاق الفريق",
            ["Audience"] = "الجمهور",
            ["PublicHostname"] = "النطاق العام",
            ["Enable"] = "تفعيل",
            ["Disable"] = "تعطيل",

            // Dialogs
            ["CopyKeyTitle"] = "مفتاح API",
            ["CopyKeyMessage"] = "تم نسخ مفتاح API.\n\nأرسله في كل طلب كـ X-API-Key header.",
            ["CopyKeyError"] = "تعذر نسخ المفتاح.\n\n{0}",
            ["NewKeyTitle"] = "مفتاح API جديد",
            ["NewKeyConfirm"] = "توليد مفتاح API جديد؟\n\nسيتم رفض كل عميل لا يزال يستخدم المفتاح الحالي حتى يتم تحديثه.",
            ["NewKeyMessage"] = "تم توليد مفتاح API جديد.\n\nاستخدم نسخ مفتاح API لوضعه على الحافظة.",
            ["DeleteTitle"] = "حذف قاعدة البيانات",
            ["DeleteConfirm"] = "حذف \"{0}\"؟\n\nسيتم إزالة هذا الاتصال نهائياً.",
            ["TurnOnlineTitle"] = "تشغيل الاتصال",
            ["TurnOnlineConfirm"] = "تشغيل \"{0}\"؟\n\nسيقوم ByteBridge باختبار اتصال قاعدة البيانات أولاً.",
            ["TurnOfflineTitle"] = "إيقاف الاتصال",
            ["TurnOfflineConfirm"] = "إيقاف \"{0}\"؟",
            ["ConnectionFailed"] = "فشل الاتصال",
            ["ConnectionFailedMessage"] = "فشل اتصال قاعدة البيانات.\n\nسيبقى الاتصال غير متصل.",
            ["ConnectionFailedError"] = "فشل اتصال قاعدة البيانات.\n\n{0}",
            ["InvalidPort"] = "الرجاء إدخال منفذ صالح بين 1 و 65535.",
            ["ServiceError"] = "تعذر بدء خدمة ByteBridge.\n\n{0}",
            ["ServiceTitle"] = "الخدمة",
            ["StopService"] = "إيقاف الخدمة",
            ["StopServiceConfirm"] = "إيقاف الخدمة يُخرج البوابة عن العمل. سيُرجع أي نفق موجّه إلى هذا الجهاز خطأ 502 حتى تُشغَّل من جديد.\n\nهل تريد إيقافها؟",
            ["ServiceStopError"] = "تعذر إيقاف خدمة ByteBridge.\n\n{0}",
            ["ServiceStoppedPrompt"] = "خدمة ByteBridge لا تعمل، فالبوابة خارج الخدمة وأي نفق موجّه إلى هذا الجهاز سيُرجع خطأ 502.\n\nهل تريد تشغيلها الآن؟",
            ["ServiceNotInstalledMessage"] = "خدمة ByteBridge غير مثبتة على هذا الجهاز، لذا لا يمكن تشغيل البوابة. أعد تثبيت ByteBridge لإضافتها.",

            // OAuth dialogs
            ["OAuthDisabled"] = "تم تعطيل تسجيل الدخول عبر Cloudflare OAuth.\n\nسيحتاج المستخدمون إلى استخدام مفتاح API للمصادقة.",
            ["OAuthEnabled"] = "تم تفعيل تسجيل الدخول عبر Cloudflare OAuth.\n\nتأكد من إعداد Cloudflare Access\nمزوّد هوية وإنشاء تطبيق\nلنطاق اسم بوابتك.",
            ["OAuthTeamDomainRequired"] = "الرجاء إدخال نطاق فريق Cloudflare Access.\n\nمثال: my-team.cloudflareaccess.com",
            ["OAuthAudienceRequired"] = "الرجاء إدخال علامة جمهور تطبيق Access.\n\nاعثر عليها في Zero Trust → Access → Applications → Settings.",
            ["OAuthPublicHostnameRequired"] = "الرجاء إدخال النطاق العام الذي يعرضه Cloudflare Tunnel.\n\nمثال: api.yourcompany.com\n\nهذا هو المكان الذي يعيد Cloudflare Access توجيه الزوار إليه بعد تسجيل الدخول — بدونه لا يمكن إتمام تسجيل الدخول.",

            // Close dialog
            ["CloseTitle"] = "ByteBridge",
            ["CloseMessage"] = "ماذا تريد أن تفعل؟",
            ["MinimizeToTray"] = "تصغير إلى صينية النظام",
            ["ExitApp"] = "خروج",
            ["Cancel"] = "إلغاء",

            // Tray icon
            ["TrayOpen"] = "فتح ByteBridge",

            // Settings
            ["AutoStart"] = "البدء مع Windows",
            ["AutoStartHint"] = "سيبدأ ByteBridge تلقائياً عند تسجيل الدخول.",
            ["LockoutAttempts"] = "الحظر بعد مفاتيح خاطئة",
            ["LockoutHint"] = "عدد المفاتيح الخاطئة التي يجوز لمتصل واحد إرسالها خلال {0} ثانية قبل رفضه لفترة. القيمة 0 تعطّل الحظر.",
            ["LockoutMinutes"] = "مدة الحظر (بالدقائق)",
            ["InvalidLockout"] = "عدد المحاولات رقم من 0 إلى 10000، ومدة الحظر من 1 إلى 1440 دقيقة.",
            ["DontAskAgain"] = "لا تسأل مرة أخرى",
            ["AskBeforeClosing"] = "السؤال عند الإغلاق",
            ["AskBeforeClosingHint"] = "الاختيار بين التصغير إلى الصينية والخروج عند كل إغلاق للنافذة.",
            ["AllowWriting"] = "السماح بالكتابة",
            ["AllowWritingHint"] = "متوقف: البوابة تقرأ فقط. مفعّل: تنفّذ أيضاً أوامر تغيّر البيانات. يتطلب صلاحية مدير.",
            ["AllowWritingConfirm"] = "تفعيل الكتابة؟\n\nكل من يحمل مفتاح API، أو دخل عبر تسجيل دخول Cloudflare في ByteBridge، سيستطيع تغيير البيانات وحذفها، وتغيير بنية قواعدك، عبر البوابة.\n\nفعّلها فقط عند الحاجة، ثم أوقفها بعد الانتهاء.",
            ["AllowWritingNeedsAdmin"] = "المدير وحده يستطيع تغيير هذا.",
            ["AllowWritingError"] = "تعذّر تغيير الإعداد: {0}",
            ["Language"] = "اللغة",
            ["LanguageHint"] = "اختر لغة العرض",
            ["English"] = "English",
            ["Arabic"] = "العربية",
        }
    };

    private static string _currentLanguage = "en";

    public static string CurrentLanguage => _currentLanguage;

    public static void SetLanguage(string language)
    {
        _currentLanguage = language;

        var culture = language switch
        {
            "ar" => new CultureInfo("ar"),
            _ => new CultureInfo("en")
        };

        /*
         * The "ar" culture's own number format uses Eastern
         * Arabic-Indic digits (٠١٢٣...) by default. Everything in this
         * app that shows a number -- ports, request counts, database
         * paths -- reads better in the Western digits everyone here
         * actually types, so they're forced regardless of language.
         */
        culture.NumberFormat.DigitSubstitution = DigitShapes.None;
        culture.NumberFormat.NativeDigits =
            ["0", "1", "2", "3", "4", "5", "6", "7", "8", "9"];

        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
    }

    public static string Get(string key)
    {
        if (Translations.TryGetValue(
                _currentLanguage,
                out var strings) &&
            strings.TryGetValue(key, out var value))
        {
            return value;
        }

        // Fallback to English
        if (Translations.TryGetValue(
                "en",
                out var english) &&
            english.TryGetValue(key, out var fallback))
        {
            return fallback;
        }

        return key;
    }

    /*
     * A key whose English text is the key itself.
     *
     * Only for tests, and only because Get cannot tell this case from
     * a missing one: both come back as the key. Twelve entries are
     * their own translation -- "Port", "Cancel", "Edit" -- so a test
     * that needed one has to find one rather than name one, or it
     * breaks the day somebody translates the word.
     *
     * Null when the table has none, which is a legitimate answer and
     * not a failure: the property being tested still holds, there is
     * just nothing here that exposes it.
     */
    internal static string? FindKeyWhoseEnglishTextIsItsOwnName()
    {
        if (!Translations.TryGetValue("en", out var english))
        {
            return null;
        }

        foreach (var (key, value) in english)
        {
            if (value == key)
            {
                return key;
            }
        }

        return null;
    }

    public static string Format(string key, params object[] args)
    {
        var template = Get(key);
        return string.Format(template, args);
    }

    /*
     * Get, with the caller's own text for a key the tables do not have.
     *
     * Written because a caller got this backwards once: it tested
     * !IsNullOrWhiteSpace and so replaced every translation it was
     * given with the English fallback -- an Arabic string that existed,
     * was fetched, and was then thrown away. The condition here is the
     * only correct one: a non-blank result is already the right
     * language, because Get falls back to English itself when the
     * current language has no entry.
     *
     * For a build whose tables predate a key entirely, so the person
     * is told something rather than shown the key name.
     */
    public static string GetOrDefault(string key, string fallback)
    {
        /*
         * Asked of the tables rather than of Get's answer, because the
         * answer cannot be told apart from the key.
         *
         * Get returns the key itself when neither table has it -- a
         * deliberate convention, so a window built by a newer source
         * than its tables shows something identifying rather than an
         * empty label. But twelve entries are their own translation:
         * "Port" is "Port", "Cancel" is "Cancel". Comparing the
         * returned text with the key to spot a miss therefore discards
         * twelve perfectly good translations and hands the caller the
         * fallback instead. Looking the key up is the only question
         * that has an answer either way.
         *
         * The two-step is Get's own: this language first, then
         * English, which is where a missing translation falls back there
         * too.
         */
        if (Translations.TryGetValue(
                _currentLanguage,
                out var strings) &&
            strings.TryGetValue(key, out var value) &&
            !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if (Translations.TryGetValue(
                "en",
                out var english) &&
            english.TryGetValue(key, out var fallbackText) &&
            !string.IsNullOrWhiteSpace(fallbackText))
        {
            return fallbackText;
        }

        /*
         * Last resort, and not the caller's text when the caller
         * supplied none: an empty label is the one outcome this
         * method exists to prevent, so a blank fallback falls back
         * again to what Get would have shown.
         */
        return string.IsNullOrWhiteSpace(fallback) ? key : fallback;
    }
}
