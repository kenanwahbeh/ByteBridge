# Using ByteBridge day to day

This page walks through everything you are likely to do in the
ByteBridge window, in the order you will do it the first time.

## 1. Install it

1. Download `ByteBridge-<version>-x64-setup.exe` from the
   [latest release](https://github.com/kenanwahbeh/ByteBridge/releases/latest).
   If you are unsure which file to take, it is that one.
2. Run it. Windows asks whether to allow changes — answer **Yes**.
   ByteBridge needs this once, because it installs a background service
   and protects the folder where your passwords are kept.
3. When Setup offers to install **cloudflared**, say yes unless your
   developer told you otherwise. That is the program that runs the
   private tunnel.

When it finishes, ByteBridge opens, and its icon appears next to the
clock at the bottom-right of the screen.

## 2. Look at the window

- **The menu bar** at the top: *File*, *Edit*, *Web Server*,
  *Cloudflare Tunnel*, *Options* and *Help*.
- **The status line** under it. When everything is fine it is green and
  reads *● Answering — http://127.0.0.1:8080 · Service: running*.
  The button on its right starts or stops the service.
- **One card per database** you have added, each showing whether it is
  **Online** or **Offline** and how many requests it has answered.

## 3. Add your database

Choose **File → New Database…**. A short wizard asks four things:

1. **Connection Name** — any name you will recognise, such as
   `Sales` or `Main shop`. Your developer uses this name in requests,
   so tell them what you chose.
2. **Server Details** — where the database lives:
   - **Server**: `127.0.0.1` if the database is on this same computer,
     otherwise the name or address of the computer that holds it.
   - **Port**: usually `3050`. Leave it unless you were told otherwise.
   - **Database**: the file path of the database (it usually ends in
     `.fdb`), or its short alias name.
3. **Credentials** — the database user name and password. These are
   the database's own, not your Windows password.
4. **Test & Finish** — press **Test Connection**. Only press **Finish**
   once it says *Connection succeeded.*

If you do not know the server, path, user or password, the person or
company that installed your accounting or business software does. Ask
them for "the Firebird connection details".

## 4. Online and Offline

A database you add is **Online** as soon as you press **Finish**, as
long as the test succeeded. Online means ByteBridge answers requests
about it.

The word on the card tells you where things stand:

| Card shows | Meaning |
| --- | --- |
| **● Online** in green | Switched on and the database is answering. |
| **● Offline** in grey | Switched off. Nothing is answered for it. |
| **● Offline** in red | Switched on, but the database is not answering — see [When something is wrong](when-something-is-wrong.md). |

The button on the card is named for what pressing it does. Press
**Offline** any time you want to stop all outside access to that
database — for example, while you are doing maintenance. It takes
effect immediately. Press **Online** to bring it back; ByteBridge tests
the connection first, and if the test fails the card stays Offline and
tells you why.

To change a database's details later, use **Edit** on its card. To
remove it, use **Delete**. Deleting removes it from ByteBridge only;
the database itself is not touched.

## 5. Give your developer the API key

Open **Web Server** from the menu bar and press **Copy API Key**. The
key is now copied, as if you had selected it and pressed Ctrl+C. Paste
it to your developer in a private message.

In the same window:

- **New Key** makes a new key and throws the old one away. Use it if
  the key may have been seen by someone it should not have been —
  a former employee, a shared chat, a lost laptop. Everything still
  using the old key stops working until it gets the new one, so tell
  your developer straight away.
- **Turn On / Turn Off** starts or stops the whole gateway. Off means
  no database answers, whatever its card says.
- **Port** is the number the tunnel connects to. Leave it at `8080`
  unless your developer asks you to change it.
- **Lock out after wrong keys** protects against someone guessing the
  key. The defaults are sensible; leave them unless asked.

## 6. The tunnel

The tunnel is usually set up once by your developer or IT person. If
they ask, **Web Server** shows the exact line they need, starting with
`cloudflared tunnel --url`.

**Cloudflare Tunnel** on the menu bar holds an optional extra lock:
*Cloudflare Login*, where people must sign in with their own account
before any request reaches your computer. Your developer will fill it
in if you decide you want it.

## 7. Options

**Options** on the menu bar has:

- **Language** — English or العربية. The window switches immediately.
- **Start with Windows** — opens this window when you sign in. The
  background service starts with Windows either way; this is only
  about the window.
- **Ask before closing** — whether closing the window asks you to
  choose between *Minimize to Tray* and *Exit*.

## 8. Closing the window

Closing the window never stops your databases from being reachable.
*Minimize to Tray* hides the window next to the clock; *Exit* closes
it completely. Either way the service keeps answering requests. To
really stop everything, use **Turn Off** in *Web Server*, or the
**Stop Service** button on the status line.

## Getting help from inside the app

- **Help → User Guide** (or the **F1** key) opens this guide, in the
  language the window is using.
- **Help → Visit ByteBalanceTech.com** opens the website of
  ByteBalanceTech, the company that makes ByteBridge.
- **Help → About ByteBridge** shows the version number — useful when
  asking for support — and has **Open Log Folder**, which opens the
  record of every request ByteBridge has answered.

Next: [When something is wrong](when-something-is-wrong.md).
