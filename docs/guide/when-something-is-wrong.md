# When something is wrong

Start with the **status line** at the top of the window. It answers
the two questions that matter: is the background service running, and
is ByteBridge actually answering?

## What the status line means

| It says | What it means | What to do |
| --- | --- | --- |
| **● Answering** (green) · *Service: running* | All good. | Nothing. |
| **● Turned off** (grey) | Someone turned the gateway off in *Web Server*. | Open **Web Server** and press **Turn On**. |
| **● Starting, or unable to bind** (orange) | The service is running but cannot answer yet. | Wait a few seconds. If it stays orange, another program is using the same port — ask your developer, or change **Port** in *Web Server* and tell them the new number. |
| **● Not running** (red) · *Service: stopped* | The background service is stopped. | Press **Start Service** on the status line. |
| *Service: not installed* | The background service is missing. | Install ByteBridge again. Your databases and key are kept. |

## A database card shows a red "Offline"

The database is switched on, but ByteBridge cannot reach it. Usually
one of these:

- The computer that holds the database is off, or the database server
  program on it is stopped. Start it again; the card turns green by
  itself within a minute or so.
- Its password or path changed, or it was saved without a successful
  test. Use **Edit** on the card, then **Test Connection**.

## "Connection failed" when adding or switching on a database

The message underneath is the database's own explanation. The usual
ones:

- *unable to complete network request* — the **Server** or **Port** is
  wrong, or the database server program is not running.
- *Your user name and password are not defined* — the user name or
  password is wrong.
- *I/O error … open* or *file … not found* — the **Database** path is
  wrong, or points to a file on another computer.

## Your developer reports an error number

Developers see short numbers. What they mean for you:

| Number | In plain words | Usually fixed by |
| --- | --- | --- |
| **502** or Cloudflare **1033** | The tunnel reached the computer, but nothing answered. | Checking the status line is green. The computer must be on and awake. |
| **401** | The password (API key) is wrong or missing. | Sending them the current key again — perhaps someone pressed **New Key**. |
| **429** | Too many wrong keys from them, so they are locked out for a while. | Waiting a minute, then using the correct key. |
| **409** | That database is switched **Offline**. | Pressing **Online** on its card. |
| **404** with a database name | No database has that name. | Telling them the exact **Connection Name** you used. |

## Before you ask for help

Have these ready; they answer most questions straight away:

1. The version number, from **Help → About ByteBridge**.
2. Exactly what the status line says, or a screenshot of the window.
3. The log: **Help → About ByteBridge → Open Log Folder** opens a
   folder with one file per day. Send the file for the day the problem
   happened.

The log records each request and the text of the query it ran. That
text names your tables and columns, and if a developer wrote a value
straight into a query — a customer's name or email, say — that value
is in the log too. Open the file and look before you send it, and send
it only to people you trust.

**Never send the API key or a database password** to anyone offering
help unless they are the person who is meant to have it. Nobody
working on ByteBridge will ever ask you for them.

## Words you may hear

| Word | Meaning |
| --- | --- |
| **Database** | Where your business software keeps its information. ByteBridge works with *Firebird* and *PostgreSQL* databases. |
| **API** | A way for one program to ask another for information. ByteBridge gives your database one. |
| **API key** | The password every request must carry. |
| **Gateway** | Another name for the part of ByteBridge that answers requests. |
| **Service** | A program Windows runs in the background, with or without anyone signed in. |
| **Tunnel** / **cloudflared** | The private connection from Cloudflare into your computer, and the program that runs it. |
| **Port** | A numbered "door" on the computer. ByteBridge only listens on one inside the computer itself (`127.0.0.1`), never on your network. |
| **Online / Offline** | Whether ByteBridge will answer requests for a particular database. |
| **Tray** | The row of small icons next to the clock. |
