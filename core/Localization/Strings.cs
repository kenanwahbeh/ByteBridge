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
            ["MenuOptions"] = "Options",
            ["MenuHelp"] = "Help",
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
            ["WizardBack"] = "Back",
            ["WizardNext"] = "Next",
            ["WizardTestConnection"] = "Test Connection",
            ["WizardTestSucceeded"] = "Connection succeeded.",
            ["WizardTestFailed"] = "Connection failed.\n\n{0}",
            ["WizardFinish"] = "Finish",
            ["WizardSave"] = "Save",

            // Web Server / Cloudflare Tunnel / About dialogs
            ["WebServerWindowTitle"] = "Web Server",
            ["CloudflareTunnelWindowTitle"] = "Cloudflare Tunnel",
            ["AboutTitle"] = "About ByteBridge",
            ["AboutVersion"] = "Version {0}",
            ["AboutDescription"] = "HTTP gateway for your databases.",
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
            ["NoDatabases"] = "No databases configured.\n\nClick + Add Data to create a connection.",
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
            ["MenuOptions"] = "خيارات",
            ["MenuHelp"] = "تعليمات",
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
            ["WizardBack"] = "السابق",
            ["WizardNext"] = "التالي",
            ["WizardTestConnection"] = "اختبار الاتصال",
            ["WizardTestSucceeded"] = "نجح الاتصال.",
            ["WizardTestFailed"] = "فشل الاتصال.\n\n{0}",
            ["WizardFinish"] = "إنهاء",
            ["WizardSave"] = "حفظ",

            // Web Server / Cloudflare Tunnel / About dialogs
            ["WebServerWindowTitle"] = "سيرفر ويب",
            ["CloudflareTunnelWindowTitle"] = "نفق Cloudflare",
            ["AboutTitle"] = "حول ByteBridge",
            ["AboutVersion"] = "الإصدار {0}",
            ["AboutDescription"] = "بوابة HTTP لقواعد بياناتك.",
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
            ["NoDatabases"] = "لا توجد قواعد بيانات مُعدّة.\n\nانقر على + إضافة بيانات لإنشاء اتصال.",
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

    public static string Format(string key, params object[] args)
    {
        var template = Get(key);
        return string.Format(template, args);
    }
}
