"""
Draws the illustrated screens used by the picture guide in docs/.

    python scripts/docs-screens.py

Writes docs/assets/screens/en/*.svg and docs/assets/screens/ar/*.svg.

Drawings rather than screenshots, on purpose: a screenshot of a real
install shows someone's real connection names and paths, and would have
to be retaken by hand, in both languages, every time a window changes.
These are laid out from the XAML (sizes, margins, colours) with made-up
example data, and the Arabic set is the English one mirrored the way
WPF mirrors a window whose FlowDirection is RightToLeft.

The labels are copied from core/Localization/Strings.cs. When a label
or a window changes there, change it here and run this again.
"""

from html import escape
from pathlib import Path

OUT = Path(__file__).resolve().parent.parent / "docs" / "assets" / "screens"

TEXT = {
    "en": {
        "file": "File", "edit": "Edit", "webserver": "Web Server",
        "tunnel": "Cloudflare Tunnel", "options": "Options", "help": "Help",
        "new_db": "New Database…", "exit": "Exit",
        "answering": "● Answering — http://127.0.0.1:8080",
        "svc_running": "Service: running",
        "stop_service": "Stop Service",
        "no_db_1": "No databases configured.",
        "no_db_2": "Use File → New Database… to create a connection.",
        "add_title": "Add Database",
        "steps": ["Connection Name", "Server Details", "Credentials", "Test & Finish"],
        "s1_title": "Connection Name",
        "s1_hint": "Give this connection a name you'll recognize.",
        "conn_name": "Connection Name",
        "sample_name": "Sales",
        "s2_title": "Server Details",
        "server": "Server", "port": "Port", "database": "Database",
        "db_hint": "Enter the database alias or database path.",
        "s3_title": "Credentials",
        "username": "Username", "password": "Password",
        "pw_hint": "This is the database password.",
        "s4_title": "Test & Finish",
        "s4_hint": "Run a test before saving, so a typo doesn't turn into a connection that never comes online.",
        "test": "Test Connection",
        "test_ok": "Connection succeeded.",
        "cancel": "Cancel", "back": "Back", "next": "Next", "finish": "Finish",
        "online": "● Online", "offline": "● Offline",
        "btn_online": "Online", "btn_offline": "Offline",
        "btn_edit": "Edit", "btn_delete": "Delete",
        "requests_0": "0 requests", "requests_n": "12 requests",
        "ws_title": "Web Server",
        "ws_hint": "Point the tunnel here:  cloudflared tunnel --url http://127.0.0.1:8080",
        "lock_attempts": "Lock out after wrong keys",
        "lock_hint": "How many wrong API keys one caller may send within 60 seconds before being refused for a while. 0 turns it off.",
        "lock_minutes": "Lock out for (minutes)",
        "copy_key": "Copy API Key", "new_key": "New Key",
        "done": "Done", "turn_off": "Turn Off",
        "copied_title": "API Key",
        "copied_1": "API key copied.",
        "copied_2": "Send it on every request as the X-API-Key header.",
        "ok": "OK",
    },
    "ar": {
        "file": "ملف", "edit": "تعديل", "webserver": "سيرفر ويب",
        "tunnel": "نفق Cloudflare", "options": "خيارات", "help": "تعليمات",
        "new_db": "قاعدة بيانات جديدة...", "exit": "خروج",
        "answering": "● يعمل — http://127.0.0.1:8080",
        "svc_running": "الخدمة: تعمل",
        "stop_service": "إيقاف الخدمة",
        "no_db_1": "لا توجد قواعد بيانات مُعدّة.",
        "no_db_2": "استخدم ملف ← قاعدة بيانات جديدة... لإنشاء اتصال.",
        "add_title": "إضافة قاعدة بيانات",
        "steps": ["اسم الاتصال", "تفاصيل السيرفر", "بيانات الدخول", "اختبار وإنهاء"],
        "s1_title": "اسم الاتصال",
        "s1_hint": "أعطِ هذا الاتصال اسماً تتعرف عليه.",
        "conn_name": "اسم الاتصال",
        "sample_name": "المبيعات",
        "s2_title": "تفاصيل السيرفر",
        "server": "السيرفر", "port": "المنفذ", "database": "قاعدة البيانات",
        "db_hint": "أدخل اسم قاعدة البيانات المستعار أو مسارها.",
        "s3_title": "بيانات الدخول",
        "username": "اسم المستخدم", "password": "كلمة السر",
        "pw_hint": "هذه كلمة سر قاعدة البيانات.",
        "s4_title": "اختبار وإنهاء",
        "s4_hint": "قم بالاختبار قبل الحفظ، حتى لا يتحول خطأ إملائي إلى اتصال لن يعمل أبداً.",
        "test": "اختبار الاتصال",
        "test_ok": "نجح الاتصال.",
        "cancel": "إلغاء", "back": "السابق", "next": "التالي", "finish": "إنهاء",
        "online": "● متصل", "offline": "● غير متصل",
        "btn_online": "متصل", "btn_offline": "غير متصل",
        "btn_edit": "تعديل", "btn_delete": "حذف",
        "requests_0": "0 طلب", "requests_n": "12 طلب",
        "ws_title": "سيرفر ويب",
        "ws_hint": "وجّه النفق هنا:  cloudflared tunnel --url http://127.0.0.1:8080",
        "lock_attempts": "الحظر بعد مفاتيح خاطئة",
        "lock_hint": "عدد المفاتيح الخاطئة التي يجوز لمتصل واحد إرسالها خلال 60 ثانية قبل رفضه لفترة. القيمة 0 تعطّل الحظر.",
        "lock_minutes": "مدة الحظر (بالدقائق)",
        "copy_key": "نسخ مفتاح API", "new_key": "مفتاح جديد",
        "done": "تم", "turn_off": "إيقاف",
        "copied_title": "مفتاح API",
        "copied_1": "تم نسخ مفتاح API.",
        "copied_2": "أرسله في كل طلب كـ X-API-Key header.",
        "ok": "موافق",
    },
}

FONT = "'Segoe UI', Tahoma, Arial, sans-serif"
BLUE = "#4169E1"      # Brushes.RoyalBlue, the app's accent
PRIMARY = "#005FB8"   # WPF-UI light theme primary button
MARK = "#E5484D"      # callout colour; not part of the app
GREEN = "#008000"
GRAY = "#808080"
LINE = "#DCDCDC"


class Canvas:
    """
    Lays a window out in left-to-right coordinates and, for Arabic,
    mirrors every x on the way out, so each screen is described once.
    """

    def __init__(self, lang, width, height, title):
        self.rtl = lang == "ar"
        self.w = width
        self.h = height
        self.parts = []
        self.marks = []
        self._chrome(title)

    def _x(self, x, w=0):
        return self.w - x - w if self.rtl else x

    def rect(self, x, y, w, h, fill="#FFFFFF", stroke=None, r=4, sw=1):
        s = f' stroke="{stroke}" stroke-width="{sw}"' if stroke else ""
        self.parts.append(
            f'<rect x="{self._x(x, w)}" y="{y}" width="{w}" height="{h}" '
            f'rx="{r}" fill="{fill}"{s}/>')

    def line(self, x1, y1, x2, y2, color=LINE):
        self.parts.append(
            f'<line x1="{self._x(x1)}" y1="{y1}" x2="{self._x(x2)}" y2="{y2}" '
            f'stroke="{color}"/>')

    def text(self, x, y, s, size=13, color="#1B1B1B", weight="normal",
             anchor="start"):
        # With direction="rtl", SVG already reads "start" as the right-hand
        # end of the text, so the anchor stays as written.
        d = ' direction="rtl"' if self.rtl else ""
        self.parts.append(
            f'<text x="{self._x(x)}" y="{y}" font-size="{size}" '
            f'font-weight="{weight}" fill="{color}" text-anchor="{anchor}"{d}>'
            f'{escape(s)}</text>')

    def button(self, x, y, w, label, primary=False, h=32):
        fill = PRIMARY if primary else "#FBFBFB"
        stroke = PRIMARY if primary else "#D0D0D0"
        self.rect(x, y, w, h, fill=fill, stroke=stroke)
        self.text(x + w / 2, y + h / 2 + 5, label, size=13,
                  color="#FFFFFF" if primary else "#1B1B1B", anchor="middle")

    def field(self, x, y, w, value, h=36):
        self.rect(x, y, w, h, fill="#FFFFFF", stroke="#C8C8C8")
        self.line(x + 1, y + h - 1, x + w - 1, y + h - 1, color="#8A8A8A")
        self.text(x + 10, y + h / 2 + 5, value, size=13)

    def mark(self, n, x, y, w, h, corner="end"):
        """
        A numbered callout around the thing to press. The number sits on
        the trailing top corner, clear of the label above a field, unless
        corner="start" asks for the leading one.
        """
        self.marks.append((n, x, y, w, h, corner))

    def _chrome(self, title):
        self.rect(0, 0, self.w, self.h, fill="#FFFFFF", stroke="#B4B4B4", r=8)
        self.rect(0, 0, self.w, 32, fill="#F3F3F3", r=8)
        self.rect(0, 20, self.w, 12, fill="#F3F3F3", r=0)
        self.line(0, 32, self.w, 32)
        self.rect(12, 9, 14, 14, fill=BLUE, r=3)
        self.text(34, 21, title, size=12)
        for i, glyph in enumerate(["✕", "☐", "—"]):
            self.text(self.w - 22 - i * 44, 21, glyph, size=12,
                      color="#444444", anchor="middle")

    def render(self):
        out = list(self.parts)
        for n, x, y, w, h, corner in self.marks:
            out.append(
                f'<rect x="{self._x(x - 4, w + 8)}" y="{y - 4}" width="{w + 8}" '
                f'height="{h + 8}" rx="6" fill="none" stroke="{MARK}" '
                f'stroke-width="3"/>')
            cx = self._x(x + w + 4 if corner == "end" else x - 4)
            cy = y - 4
            out.append(
                f'<circle cx="{cx}" cy="{cy}" r="14" fill="{MARK}"/>'
                f'<text x="{cx}" y="{cy + 5}" font-size="15" font-weight="bold" '
                f'fill="#FFFFFF" text-anchor="middle">{n}</text>')
        body = "\n  ".join(out)
        return (
            f'<svg xmlns="http://www.w3.org/2000/svg" '
            f'viewBox="-20 -20 {self.w + 40} {self.h + 40}" '
            f'width="{self.w + 40}" height="{self.h + 40}" '
            f'font-family="{FONT}">\n  {body}\n</svg>\n')


def menu_bar(c, t, open_file=False):
    items = ["file", "edit", "webserver", "tunnel", "options", "help"]
    x = 6
    spots = {}
    for key in items:
        label = t[key]
        w = max(44, int(len(label) * 7.2) + 20)
        if key == "file" and open_file:
            c.rect(x, 34, w, 24, fill="#E5E5E5", r=3)
        c.text(x + w / 2, 51, label, size=13, anchor="middle")
        spots[key] = (x, 34, w, 24)
        x += w
    c.line(0, 60, c.w, 60)
    return spots


def status_line(c, t):
    c.text(20, 86, t["answering"] + "    ·    " + t["svc_running"],
           size=13, color=GREEN)
    c.button(c.w - 20 - 110, 70, 110, t["stop_service"], h=28)
    c.line(0, 108, c.w, 108)


def card(c, t, y, online, requests):
    c.rect(20, y, c.w - 40, 84, fill="#FFFFFF", stroke=LINE, r=8)
    c.text(36, y + 32, t["sample_name"], size=18, weight="600")
    if online:
        c.text(36, y + 56, t["online"], size=14, color=GREEN)
    else:
        c.text(36, y + 56, t["offline"], size=14, color=GRAY)
    c.text(36, y + 74, requests, size=12, color=GRAY)
    right = c.w - 36
    widths = [96, 70, 70]
    labels = [t["btn_offline"] if online else t["btn_online"],
              t["btn_edit"], t["btn_delete"]]
    x = right - sum(widths) - 10
    first = None
    for w, label in zip(widths, labels):
        c.button(x, y + 26, w, label)
        first = first or (x, y + 26, w, 32)
        x += w + 5
    return first


def main_empty(lang):
    t = TEXT[lang]
    c = Canvas(lang, 900, 330, "ByteBridge")
    spots = menu_bar(c, t, open_file=True)
    status_line(c, t)
    c.text(450, 190, t["no_db_1"], size=16, color=GRAY, anchor="middle")
    c.text(450, 222, t["no_db_2"], size=16, color=GRAY, anchor="middle")
    fx, fy, fw, fh = spots["file"]
    c.rect(fx, 58, 220, 74, fill="#FFFFFF", stroke="#C8C8C8", r=6)
    c.rect(fx + 4, 62, 212, 28, fill="#E8EFFA", r=4)
    c.text(fx + 16, 81, t["new_db"], size=13)
    c.line(fx + 8, 96, fx + 212, 96)
    c.text(fx + 16, 118, t["exit"], size=13)
    c.mark(1, fx, fy, fw, fh, corner="start")
    c.mark(2, fx + 4, 62, 212, 28)
    return c


def wizard(lang, step, body):
    t = TEXT[lang]
    c = Canvas(lang, 620, 470, t["add_title"])
    # Step pills, centred as a group the way the StackPanel centres them.
    widths = [30 + 8 + int(len(s) * 7) + 20 for s in t["steps"]]
    x = (620 - sum(widths) + 20) / 2
    for i, (label, w) in enumerate(zip(t["steps"], widths), start=1):
        if i == step:
            fill, num = BLUE, "#FFFFFF"
        elif i < step:
            fill, num = "#BED2F5", BLUE
        else:
            fill, num = "#E6E6E6", GRAY
        c.rect(x, 50, 30, 30, fill=fill, r=15)
        c.text(x + 15, 70, str(i), size=13, weight="bold", color=num,
               anchor="middle")
        c.text(x + 38, 70, label, size=13)
        x += w
    marks = body(c, t)
    # Buttons sit bottom-right in the XAML, which mirrors to bottom-left.
    y = 470 - 20 - 36
    last = step == 4
    names = [("cancel", 90), ("back", 90), ("finish" if last else "next", 100)]
    if step == 1:
        names = [("cancel", 90), ("next", 100)]
    x = 620 - 24 - sum(w for _, w in names) - 10 * (len(names) - 1)
    spot = None
    for i, (key, w) in enumerate(names):
        primary = i == len(names) - 1
        c.button(x, y, w, t[key], primary=primary, h=36)
        if primary:
            spot = (x, y, w, 36)
        x += w + 10
    c.mark(marks + 1, *spot)
    return c


def wizard_name(lang):
    def body(c, t):
        c.text(24, 132, t["s1_title"], size=22, weight="bold")
        c.text(24, 160, t["s1_hint"], size=13, color=GRAY)
        c.text(24, 200, t["conn_name"], size=15, weight="600")
        c.field(24, 212, 572, t["sample_name"])
        c.mark(1, 24, 212, 572, 36)
        return 1
    return wizard(lang, 1, body)


def wizard_server(lang):
    def body(c, t):
        c.text(24, 132, t["s2_title"], size=22, weight="bold")
        c.text(24, 172, t["server"], size=15, weight="600")
        c.field(24, 182, 572, "127.0.0.1")
        c.text(24, 246, t["port"], size=15, weight="600")
        c.field(24, 256, 572, "3050")
        c.text(24, 320, t["database"], size=15, weight="600")
        c.field(24, 330, 572, r"C:\Data\SALES.FDB")
        c.text(24, 386, t["db_hint"], size=12, color=GRAY)
        c.mark(1, 24, 182, 572, 36)
        c.mark(2, 24, 256, 572, 36)
        c.mark(3, 24, 330, 572, 36)
        return 3
    return wizard(lang, 2, body)


def wizard_credentials(lang):
    def body(c, t):
        c.text(24, 132, t["s3_title"], size=22, weight="bold")
        c.text(24, 172, t["username"], size=15, weight="600")
        c.field(24, 182, 572, "SYSDBA")
        c.text(24, 246, t["password"], size=15, weight="600")
        c.field(24, 256, 572, "●●●●●●●●")
        c.text(24, 312, t["pw_hint"], size=12, color=GRAY)
        c.mark(1, 24, 182, 572, 36)
        c.mark(2, 24, 256, 572, 36)
        return 2
    return wizard(lang, 3, body)


def wizard_test(lang):
    def body(c, t):
        c.text(24, 132, t["s4_title"], size=22, weight="bold")
        c.text(24, 158, t["s4_hint"], size=13, color=GRAY)
        w = int(len(t["test"]) * 7.5) + 36
        c.button(24, 180, w, t["test"], h=38)
        c.rect(24, 234, 572, 44, fill="#F2FAF2", stroke="#CDE8CD", r=4)
        c.text(36, 261, t["test_ok"], size=14, color=GREEN)
        c.mark(1, 24, 180, w, 38)
        return 1
    return wizard(lang, 4, body)


def main_offline(lang):
    t = TEXT[lang]
    c = Canvas(lang, 900, 250, "ByteBridge")
    menu_bar(c, t)
    status_line(c, t)
    spot = card(c, t, 128, online=False, requests=t["requests_0"])
    c.mark(1, *spot)
    return c


def main_online(lang):
    t = TEXT[lang]
    c = Canvas(lang, 900, 250, "ByteBridge")
    spots = menu_bar(c, t)
    status_line(c, t)
    card(c, t, 128, online=True, requests=t["requests_n"])
    c.mark(1, 20, 72, 470, 22)
    c.mark(2, 36, 170, 120, 20)
    c.mark(3, *spots["webserver"])
    return c


def web_server(lang):
    t = TEXT[lang]
    c = Canvas(lang, 560, 470, t["ws_title"])
    c.text(24, 72, t["answering"], size=16, weight="600", color=GREEN)
    c.text(24, 96, t["svc_running"], size=13, color=GRAY)
    c.text(24, 120, t["ws_hint"], size=12, color=GRAY)
    c.text(24, 170, t["port"], size=15, weight="600")
    c.field(446, 150, 90, "8080", h=34)
    c.text(24, 222, t["lock_attempts"], size=15, weight="600")
    hint = t["lock_hint"]
    cut = hint.rfind(" ", 0, 62)
    c.text(24, 242, hint[:cut], size=12, color=GRAY)
    c.text(24, 258, hint[cut + 1:], size=12, color=GRAY)
    c.field(446, 214, 90, "10", h=34)
    c.text(24, 306, t["lock_minutes"], size=15, weight="600")
    c.field(446, 286, 90, "1", h=34)
    w1 = int(len(t["copy_key"]) * 7.5) + 30
    c.button(24, 338, w1, t["copy_key"])
    w2 = int(len(t["new_key"]) * 7.5) + 30
    c.button(24 + w1 + 8, 338, w2, t["new_key"])
    c.button(560 - 24 - 110 - 10 - 90, 470 - 24 - 36, 90, t["done"], h=36)
    c.button(560 - 24 - 110, 470 - 24 - 36, 110, t["turn_off"], primary=True,
             h=36)
    c.mark(1, 24, 338, w1, 32)
    return c


def key_copied(lang):
    t = TEXT[lang]
    c = Canvas(lang, 420, 190, t["copied_title"])
    c.rect(24, 58, 32, 32, fill=BLUE, r=16)
    c.text(40, 80, "i", size=18, weight="bold", color="#FFFFFF",
           anchor="middle")
    c.text(72, 72, t["copied_1"], size=14)
    c.text(72, 104, t["copied_2"], size=13)
    c.button(420 - 24 - 90, 190 - 20 - 32, 90, t["ok"])
    return c


SCREENS = {
    "1-new-database": main_empty,
    "2-connection-name": wizard_name,
    "3-server-details": wizard_server,
    "4-credentials": wizard_credentials,
    "5-test-and-finish": wizard_test,
    "6-turn-online": main_offline,
    "7-all-good": main_online,
    "8-copy-api-key": web_server,
    "9-key-copied": key_copied,
}


def main():
    for lang in TEXT:
        folder = OUT / lang
        folder.mkdir(parents=True, exist_ok=True)
        for name, draw in SCREENS.items():
            path = folder / f"{name}.svg"
            path.write_text(draw(lang).render(), encoding="utf-8",
                            newline="\n")
            print(path.relative_to(OUT.parent.parent.parent))


if __name__ == "__main__":
    main()
