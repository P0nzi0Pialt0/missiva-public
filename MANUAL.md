# Missiva 0.3

[Italiano](LEGGIMI.md)

Email client for Windows 11, modeled on the new Outlook but native: a WPF interface with the Windows Fluent style, and
writing with a native editor and spell checking in the language of Missiva, English or Italian (below, "Language").
Only the content of emails is laid out by WebView2, locked down: no JavaScript, no Internet, like a viewer and not like
a browser. Supported accounts: Outlook.com, Hotmail and Live (personal Microsoft accounts), Microsoft 365 work or school
(with Missiva's app or with the one registered by your organization), Gmail and Google Workspace, and other IMAP and
SMTP accounts (mailboxes on your own domain: Aruba, Register.it, OVH, Zoho, cPanel, Exchange with IMAP).

Add account starts from the email address, like Outlook and Thunderbird: Missiva recognizes the type of account by
itself (from the domain, and for your own domains from the mail server) and suggests it; you can always choose it by
hand.

## Install

From a release published on GitHub (the Releases page): download `Missiva-0.3.N.zip`, extract it to a folder and from
there run `powershell -ExecutionPolicy Bypass -File installa.ps1`. Nothing else is needed: the script sees
`Missiva.exe` next to it instead of the source code, tests the program and installs it, even with the Windows
PowerShell 5.1 that comes with Windows. Missiva goes into `%LOCALAPPDATA%\Programs\Missiva`, appears in the Start menu
and in Settings, Apps, Installed apps (where you can uninstall it), and from then on updates itself (below, "Updates").
Without a code signing certificate, the first time you open a downloaded file Windows may say that it "protected your
PC" (More info, Run anyway).

From source you need the .NET 10 SDK and PowerShell 7 (the `pwsh` command: Windows doesn't include it, you install it
from the Microsoft Store or with `winget install Microsoft.PowerShell`). From the source folder: `pwsh -ExecutionPolicy
Bypass -File installa.ps1`; the result is the same, but a Missiva built this way doesn't update itself: to update it,
run the same command again.

Updates: two minutes after startup and then once a day, a Missiva installed from a release asks GitHub whether there's
a new version. If there is, the **Update** button appears in the title bar, on the right: selecting it opens "Missiva
0.3.N" with its date and what's new, Update and Later (for that version it comes back the next day). Update downloads
the new version (progress shows in the button, with the percentage, and in the small panel, with Cancel), checks it,
asks for confirmation and then closes Missiva as with Exit (messages you're writing ask whether to save the draft) and
reopens it updated. If something goes wrong, the button says "Update failed" and the panel says why. In Settings,
General, About there are the version, the **Check for updates automatically** check box (when it's off, no requests
to GitHub) and **Check now** with the result of the last check; in a Missiva built from source, in their place, a
note that it updates with `installa.ps1`. What it checks before accepting an update and what GitHub sees:
[docs/aggiornamenti.en.md](docs/aggiornamenti.en.md).

The version changes with every change: the first two numbers are written by hand in the project, the third is the
number of changes (the git commits) and grows by itself, and next to it are the date and the code of the latest change
included, for example "0.3.526 (10/14/2026, 1a2b3c4)" in Settings, General, About (`installa.ps1` and the log
write it the Italian way, "0.3.526 del 14/10/2026 (1a2b3c4)"). It's also shown by `installa.ps1` (the one that was
installed before too), Installed apps and the first line of the log at every start. Built from a copy without git (a
ZIP) it stays "0.3.0".

To open the email links (mailto:) of web pages and PDFs with Missiva, after installing choose it in Settings, Apps,
Default apps, Missiva, MAILTO: Windows doesn't let programs do it themselves.

## Language

Missiva is in English and Italian. You choose the language in Settings, General, Appearance, "Language (Lingua)": Same
as Windows (the default: Italian if Windows is in Italian, English otherwise), Italiano or English. It applies from the
next start: Restart now reopens it right away in the new language. The language changes:

- the whole interface: windows, menus, warnings and questions, notifications, the reading pane page;
- dates, months and days: in English with the Windows formats if Windows uses English ones (US, UK...), otherwise with
  the UK ones;
- the spell checker when you write;
- what goes out with your messages: the subject prefix of replies and forwards (RE: and FW: in English, R: and I: in
  Italian; a prefix already there, in either language, isn't repeated) and, with Gmail and IMAP accounts, the line
  above the quoted message ("On Wednesday, October 21, 2026 2:30 PM, Mario Rossi <...> wrote:", or "Il mercoledì 21
  ottobre 2026 14:30, Mario Rossi <...> ha scritto:" in Italian; with Microsoft accounts the Outlook server writes it);
- search keywords: in English from:, to:, subject:, after:, before:, has:attachment, is:unread and is:flagged also
  work, and dates are written the English way; the Italian ones (da:, a:, oggetto:, dal:, al:...) always work.

In templates, {name} and {date} can also be written {nome} and {data}, in both languages.

## Microsoft accounts: the first time

Add account, type the address (outlook.com, hotmail.com...), Sign in with Microsoft: the Windows account window opens;
choose the account and allow Missiva to read and send mail.

Missiva uses the official Microsoft Graph interface, which needs an "Application ID" registered on Microsoft Entra.
Missiva already has one. Only if you prefer to use an app you registered yourself (free, once, valid for all personal
Microsoft accounts):

1. Activate a free Azure account with your personal Microsoft account: https://azure.microsoft.com/free (Microsoft
   asks for card verification, with no charges; since apps can no longer be registered outside a directory, it's the
   only way for a personal account). Then sign in to https://entra.microsoft.com.
2. Entra ID, App registrations, New registration.
3. Name: Missiva. Supported account types: "Personal Microsoft accounts only". Redirect URI: leave it empty. Select
   Register.
4. On the Overview page copy the Application (client) ID.
5. Authentication, Add a platform, Mobile and desktop applications. In the custom redirect URIs write, replacing your
   ID: `ms-appx-web://microsoft.aad.brokerplugin/YOUR-ID`
   Select Configure. Then add, in the same section, `http://localhost` too, and save.
6. API permissions, Add a permission, Microsoft Graph, Delegated permissions: check Mail.ReadWrite and Mail.Send, then
   Add permissions. They're enough for mail. The other features each ask for one more permission, only when you
   select their "Allow access": Calendars.ReadWrite (calendar and invitations), Contacts.ReadWrite (People),
   MailboxSettings.ReadWrite (automatic replies, Outlook rules and categories), People.Read (colleagues),
   Mail.ReadWrite.Shared and Mail.Send.Shared (shared mailboxes). Missiva's app has them all declared here; with your
   own app, if you want to use them, add them the same way (without declaring them it has never been tested).
7. In Missiva: Add account, type the address (or choose Microsoft), paste the ID in Missiva's Microsoft app, Sign in
   with Microsoft. The Windows account window opens.

## Work or school accounts (Microsoft 365)

Type the address in Add account: if the domain uses Microsoft 365, Missiva recognizes it. Missiva's app is registered
for personal accounts and for work or school ones: Sign in with Microsoft opens the Windows account window. If your IT
department has registered an app for Missiva, you can use that one: "Use my organization's app", with the app's ID and
the directory (tenant) ID or domain. It applies only to that account: personal ones keep using Missiva's app. The
organization's app needs the Mobile and desktop applications platform with the redirect URIs
`ms-appx-web://microsoft.aad.brokerplugin/APP-ID` and `http://localhost`, and the delegated permissions User.Read,
Mail.ReadWrite and Mail.Send.

If Microsoft refuses the sign-in, Missiva explains why. When an administrator's approval is needed (your organization
doesn't let users consent to apps, or only to apps from verified publishers), "Ask an admin for approval" gives you the
link to send them: you copy it, or put it in a new message from another account of yours. With Missiva's app a single
consent covers all its permissions: mail, and also calendar, mailbox settings and shared mailboxes, which Missiva asks
for only when you open those features. Once they've approved it, add the account again. The same goes if the
organization refuses a permission asked for later (for example "Allow access to calendar").

Shared mailboxes (info@, office@, accounts@): right-click the work account, "Add shared mailbox…", and type the address
(Microsoft doesn't tell programs which mailboxes you have access to). The mailbox appears under the account with its
folders, search and the copy for reading offline; the first time, "Allow access to shared mailboxes" under it opens the
Microsoft window (only when you select it). Replies to its messages go out from the shared mailbox, with the copy in its
Sent Items. Your administrator must have given you Full Access to the mailbox, and Send As (or Send on Behalf) to write
from there: if it's missing, Missiva says so. Removing the account also removes its shared mailboxes.

## Manage account

In Settings, Accounts, each account has Manage (also from right-clicking the account): the name shown in the From
field; for other accounts (IMAP) the password and the servers, which Missiva tests with the server before saving them
(a wrong password doesn't change anything); for Microsoft and Google "Sign in again". Signatures, categories, Favorites
and folder order stay. When the server refuses the password (for example the Aruba PEC password for mail programs,
which expires every three months) or Microsoft and Google ask for a new sign-in, a bar appears under the account with
"Update password" or "Sign in again": nothing opens by itself, not even with the Missiva window open (the mail check,
the changes every 30 seconds and the copy of the last 30 days don't open the Microsoft window). If you select Cancel in
the Microsoft window, it doesn't come back by itself: Sign in again reopens it.

## Other accounts (IMAP and SMTP)

For mailboxes on your own domain. Type address, name and password: Missiva looks up the servers by itself (the domain's
configuration, Thunderbird's provider database, DNS records) and shows them before signing in; in Manual setup you can
write or correct them (server, port, SSL/TLS or STARTTLS, user name). If the servers found in DNS belong to another
domain, Missiva says so and asks for confirmation before sending the password. For Aruba PEC and Legalmail certified
email (also on your own domains) the servers are already known. The connection is always encrypted, and a server with
an invalid certificate is refused, with the explanation. If the mailbox doesn't have a Deleted Items folder yet,
Missiva creates it the first time you delete something, like Thunderbird. The password stays on the PC, encrypted with
Windows data protection and readable only by your Windows user, and you change it in Manage account. With your
provider's two-step verification you need an app password.

## Gmail and Google Workspace: the first time

Signing in works like in the new Outlook: the browser opens, you choose your Google account and allow Missiva to use
your mail. But Google allows this only for apps registered on Google Cloud: each person registers their own, once, for
free, and it works for all their Gmail accounts.

The step-by-step guide is in [docs/gmail.en.md](docs/gmail.en.md), "Gmail: register your own app on Google Cloud": the
seven steps, Google's page warning about the unverified app, calendar and contacts (optional), Google Workspace, what to
do if something goes wrong, what stays on the PC and how to remove access. The home page (README.en.md) points there
too: if the steps change, they're fixed only in that file and in its Italian version, [docs/gmail.md](docs/gmail.md).

## What Missiva does

- A single bar at the top, like the new Outlook: name, search and the Windows buttons (with Snap Layouts); below, the
  Home and View tabs, a column with Mail, Calendar and People, the folders of all accounts (each account collapses
  with its arrow), the message list, the reading pane.
- The window adapts down to half the screen: below a certain width the folders close and the button with three lines
  opens them over the list, list and reading pane always stay inside the window (in a narrow window the list shrinks
  first: the reading pane stays the widest column), and the commands that don't fit in the bar go into the "..." menu.
- Folders in a tree, with subfolders that open and close. You reorder them by dragging (a line shows where they'll go)
  or by right-clicking, Move up and Move down; the order is saved for each account.
- A list like the new Outlook: messages grouped by conversation (an arrow opens them), date headers (Today, Yesterday,
  This week...), messages pinned at the top, quick actions when you hover and on the row chosen with the arrow keys
  (mark as read or unread, flag, pin, delete), a check box in place of the sender's picture, attachment previews. In
  Sent Items and Drafts the row shows the recipients instead of the sender, and drafts have "[Draft]" in red before the
  subject. All rows have the same height, even without a preview or while it arrives: the list doesn't move under the
  mouse. The list of Gmail and other accounts is in time order, as it's shown. At startup the open folder is
  highlighted, and with more than one account the account appears next to the folder name.
- Sender logos instead of initials, in the list, in the message, on the person's card and in notifications: the brand's
  certified logo (the one Gmail shows too) or the site's icon, only for companies you've been getting mail from for at
  least two weeks and whose messages your mailbox's server considers authentic (they passed the DMARC check: known with
  Gmail and Outlook, not with an IMAP account; a company that doesn't publish DMARC keeps its initials). Never for
  personal mail (gmail.com and the like), certified email or junk.
  Missiva downloads the logo from the sender's site when the row appears in the list, without telling them which
  message you received, keeps it on the PC and asks for it again after 30 days. You turn it off in Settings, Mail,
  Message list, "Show the sender's company logo".
- Narrator and UI Automation read rows like Outlook ("Unread, Amazon, Shipped: ..., today 09:54, has attachment,
  flagged") and folders ("Inbox, 3 unread"); every button with an icon has a name.
- Focused and Other Inbox for Microsoft accounts, which you can turn on for each account.
- A reading pane with the whole conversation (the latest message at the top, the others open with a click), on the
  right, at the bottom or hidden (messages then open in a window with a double-click or Enter). Like the new Outlook it
  changes in one go: subject, image notice and message text arrive together, without flicker. Like the new Outlook,
  Missiva downloads in advance the messages you'll probably open (for Gmail the first ten in the list and those next to
  the open message, for Outlook.com the next one), without marking them as read: they open in a few hundredths of a
  second.
- Emails laid out as in the original, newsletters included; those wider than the pane shrink to fit, and if that's not
  enough (at half screen) they scroll sideways, with the bar at the bottom of the pane.
- Dark theme: emails that have a dark version use it, the others are adapted to dark colors as in Outlook; the button
  with the sun shows them on a light background, and View has "Light reading pane" to always see them that way. Zoom
  for the message text.
- Settings like Outlook (General: Appearance, Notifications, Startup and exit, Backup, About; Accounts: Email accounts
  and Signatures; Mail: Message list, Reading pane, Message handling, Rules, Compose and reply and Templates), opened
  from the gear at the bottom of the left column: theme, language, notifications per account and sound, the number on
  the icon, text size, conversations, a preview of 0, 1 or 2 lines, sender or subject first, sender pictures,
  attachment previews, date headers, when to mark opened messages as read. They apply right away (the language at the
  next start).
- Messages marked as read right away, after a few seconds open in the reading pane, when the selection changes, or
  never (Settings, Mail, Message handling).
- Reading with attachments between the header and the text, like the new Outlook: file type icon and size, click to
  open, a button next to it to save, "Save all" to a folder (except the file types that can start programs, marked in
  red: those are saved one by one). Attachments opened with a program or saved, messages saved as .eml and the
  documents of signed files go through the downloads in the title bar (below). An attached web page (.html, .htm,
  .svg, .mht) opens in the browser only after a question: it's a page that came by email, don't type passwords into
  it. External images are blocked until you ask for them, replaced by a gray box with the icon and the alternative text
  (at most 160 pixels tall, so the text shows right away): after Show images, they're always shown for that sender, in
  all accounts (except in Junk Email, in messages with a red bar about the sender or the signature, and in attached
  messages, the .eml files, where they never load by themselves, not even for allowed senders: there Show images works
  only that once). For companies the whole domain counts (google.com includes accounts.google.com), for personal mail
  like gmail.com only that address. For bodies under gov.it, edu.it or an Italian place name (a town council under
  pd.it, a body under veneto.it or padova.it) that body counts, not everyone under the same suffix. An entry that would
  cover a whole mail service (gmail.com) or a whole suffix (co.uk, pd.it), which Missiva doesn't write, is removed at
  startup, and the log says which one: for those senders select Show images again.
  The list is in Settings, Mail, Reading pane.
- mailto: links in emails open a new message in Missiva, and so do those of web pages and PDFs if you choose Missiva as
  your mail app (see Install).
- Unsubscribe, like the new Outlook and Gmail: in newsletters that support it, under the sender there's "Getting too
  many messages from this sender? Unsubscribe". Missiva first tells you what it will do and toward which site, and does
  nothing without your confirmation: it sends the request to the sender's site (only if the message really comes from
  them, that is it passed the DMARC check, and carries their signature: known with Gmail and Outlook, not with an IMAP
  account), or prepares a message that you send yourself (from the address the newsletter writes to, if it's one of
  your other addresses), or opens the sender's page in the browser. Messages that already arrived stay where they are.
  The command isn't there in Junk Email (unsubscribing there confirms your address to whoever sends spam) or in
  messages with the red bar.
- Who really wrote: when replies go to another address (Reply-To, for example website forms) the message says so
  ("Replies go to"), and Reply goes there as in Outlook. A red bar warns when your mailbox's server couldn't verify the
  sender (DMARC failed, or SPF and DKIM both failed), a yellow one when the name belongs to someone you've written to
  but the address is a different one, or when the sender presents themselves with someone else's address or site
  instead of a name (then the list and notifications show the real address). In work accounts, senders outside the
  organization have "External", and when you write to external people Missiva reminds you.
- Meeting invitations: a card in the message with when (in your PC's time), where, organizer, attendees, recurrence
  and the online meeting link (Teams, Zoom, Meet), and Accept, Tentative, Decline (with a note, with or without a reply
  to the organizer) and Propose new time. For Gmail and other accounts the reply goes out as a message to the
  organizer, which their calendar understands; for Outlook invitations you need "Allow access to calendar" once, and
  the reply also updates the Outlook calendar. Replies to invitations you sent ("Anna accepted"), proposals for a new
  time and events from a ticket or a webinar have the card without buttons.
- Attached messages (.eml) open in a Missiva window, with the same locked-down pane. There external images are always
  blocked until you select Show images: nobody verifies who really wrote an attached message.
- Write, reply, reply all, forward, with attachments, links and formatting like Format text in the new Outlook: font
  and size, bold, italic, underline, font color and highlighter (fixed colors, which stay even in the dark theme),
  lists, indent, alignment, tables (Insert table), pictures from a file and Clear formatting. The font, size and color
  your messages start with are chosen in Settings, Mail, Compose and reply; resumed drafts and templates keep their
  formatting. A pasted screenshot (Ctrl+V) or a picture dragged into the text goes inside the message, like the new
  Outlook, and so do tables pasted from Word or Excel; files dragged onto the window or pasted become attachments.
  Below the text you see the message you're replying to or forwarding (From, Sent, To, Subject and the text, with Show
  all); a reply or forward you haven't touched closes without questions.
- Drafts like the new Outlook: they save themselves while you write (10 seconds after the last change, or every
  minute if you keep going; next to the buttons "Draft saved at..."), even when Windows restarts for updates. When you
  close a message with something unsaved Missiva offers Save draft (or Discard, or Cancel), also when you close all of
  Missiva; Ctrl+S saves whenever you want. Discard (or Esc) also removes from Drafts the draft that saved itself, and
  when you change the sender the draft moves to the other account. In Drafts, Edit (or a double-click) reopens the
  draft with recipients, subject, text and attachments; once sent, it disappears from Drafts.
- Undo send like the new Outlook: after Send the message waits 5 seconds (0 or 10 in Settings, Mail, Compose and
  reply) with "Sending… Undo" at the bottom of the list; Undo reopens it as it was.
- Forgotten attachment, like the new Outlook: if the text talks about an attachment ("attached", "enclosed", "in
  allegato") and there isn't one, on Send Missiva asks "Did you forget the attachment?", with Send anyway or Back to
  message. The signature and the message you're replying to don't count; when forwarding, the original's attachments
  do. You turn it off in Settings, Mail, Compose and reply.
- Schedule send, from the arrow next to Send: tomorrow at 8, Monday at 8 or at the time you choose. With Microsoft
  accounts the message waits in the Outlook Outbox and goes out even with Missiva closed; with Gmail and the others it
  stays in Drafts and Missiva sends it at the chosen time, or as soon as you reopen it. If it can't go out, Missiva says
  why: offline it tries again every minute, in the other cases the message stays in Drafts to be sent again. If the
  connection drops right while the server is receiving it, it doesn't try again, because it could arrive twice: it
  says it may have been sent, and you check.
- Message options: high or low importance, read and delivery receipts. Messages received with high importance have the
  red exclamation mark in the list.
- Signatures for each account and address (Settings, Accounts, Signatures), like the new Outlook: several signatures
  with a name (for example a short one), one for new messages and one for replies and forwards (or none); while
  writing, the Signature button changes it. When you change the sender the signature changes too, if you haven't
  edited it; if you chose No signature from the Signature button, the message stays without one.
- Templates, like My templates in the new Outlook (Settings, Mail, Templates): text you write again and again (quotes,
  payment reminders, bank details), with a name, the subject if you want and formatted text, for all accounts or for
  one. While writing, the Templates button searches them and inserts them where you are (and adds the subject if it's
  missing); "Save as template" takes the text you wrote. The message menu also has "Reply with template". {name}
  becomes the name of the first recipient, {date} today's date. They stay on the PC, encrypted. A template that can no
  longer be decrypted on this PC (settings brought over from another Windows user without the backup) doesn't appear
  among those to insert: it stays in Settings, Mail, Templates, where you delete it.
- Rules, like Settings, Mail, Rules in the new Outlook: the list in the order they apply, each one on or off, with
  Edit, Delete, Up, Down and "Run now" on a folder (the last 500 messages, after a confirmation with how many it
  changes or, if the rule deletes, how many go to Deleted Items). Conditions: sender (addresses or words), sent to me,
  in Cc, subject, body, attachments, importance; actions: move, copy, delete (to Deleted Items), mark as read, flag,
  category, stop processing more rules. "Create rule" in a message's right-click menu starts with its sender. With
  Microsoft accounts they're the Outlook rules, the same as Outlook on the web: they run on the server even with
  Missiva closed (the first time "Allow access to mailbox settings", only when you select it). A rule that forwards
  mail outside your organization (the trick of whoever secretly gets into a mailbox) is in red with Turn off, and
  Missiva creates it, or turns it back on if it was off, only after a confirmation with all the addresses in view,
  including those the rule window doesn't show; there Enter cancels, and to confirm you choose the button. When you
  save a rule without conditions, the question reminds you of the addresses it sends mail to.
  Outlook rules with conditions Missiva doesn't show (subject or body, recipients, exceptions) don't have Run now:
  Outlook applies them to the messages that arrive.
  With Gmail and other accounts they're Missiva rules, encrypted on the PC: they act on new messages in the Inbox only
  while Missiva is open (also in the notification area), on all those that arrived since the last time (even those
  already read on your phone, even after a night with the laptop closed), once per message; what they move or delete
  doesn't arrive as a notification. If the server refuses an action the others go ahead anyway, and Missiva tries that
  message again at the next checks (every minute), up to three times in all, then leaves it in the Inbox; a copy
  already made isn't made again. Run now says what worked and what didn't.
- Recipient suggestions in To, Cc and Bcc (and among a meeting's attendees) as you type, from the people you write to
  (Sent Items of every account), those you get mail from and your contacts in People (also those you've never written
  to, with the company and all their addresses; for Microsoft accounts also the Outlook contacts, if you've already
  given access): arrows and Enter or Tab, or a click. With a Microsoft 365 work account, "Also search your colleagues"
  at the bottom finds the colleagues in your organization you've never written to (the first time it asks for
  permission, only when you select it; if an administrator is needed, Missiva explains how to ask them).
- Contact groups (People, New group): a name and the people; typing the name in To you find it among the suggestions,
  and once chosen it becomes the addresses of its people. They stay on the PC, encrypted.
- The person's card: a click on the sender's picture or name in the reading pane (also in each message of a
  conversation) shows the contact with company, phones and notes, or "Add to contacts", with the latest messages with
  that person and their attachments, Send email and "All messages".
- Flag with a due date, like Outlook: from the message menu Today, Tomorrow, This week, Next week, No date, a chosen
  date, and the reminder at the time you want (a notification, also with Missiva in the notification area, with Snooze
  reminder and Mark complete). The row says "Due tomorrow", in red when it's past; in All accounts, Flagged sorts them
  by due date (Overdue, Today, Tomorrow, This week, Later, No date). With Microsoft accounts the dates live in Outlook;
  with Gmail and the others the flag lives on the server and the dates in Missiva.
- Delete, archive, move, copy, mark as read or unread, flag, pin. On a collapsed conversation the actions apply to all
  its messages in the folder, as in Outlook. After Delete, Archive or Move, Undo appears at the bottom of the list for
  a few seconds (also Ctrl+Z): the messages go back where they were, in Gmail with the labels they had. With Select, a
  click adds a message to the selection or removes it.
- Messages dragged onto a folder or a Favorite of the same account: they move (with Ctrl they're copied), and the
  folder under the pointer is highlighted.
- Right-click menu like the new Outlook, with colored icons; with "Customize menu…", at the bottom, you choose which
  items to show, their order and the separators.
- Categories, as in Outlook: colored chips in the list and in the message. In Gmail they're labels (Missiva keeps the
  color), in Microsoft accounts the Outlook categories (with their colors after the consent to mailbox settings, from
  the automatic replies window; before that, those created in Missiva).
- Automatic replies for Microsoft accounts, like the new Outlook (account menu or Settings, Accounts, Automatic
  replies): off, on, or on during a period, with the message for your organization and the one for external senders
  (to nobody, to contacts, to everyone; for Outlook.com a single message). When they're on or scheduled, a bar under
  the account says so, with Turn off and Edit. The first time "Allow access to mailbox settings" opens the Microsoft
  window (only when you select it).
- Snooze: the message goes to the Snoozed folder and at the chosen time comes back to the Inbox, unread and at the
  top, with a notification. Missiva brings it back: if it's closed at that time, it comes back as soon as you reopen
  it.
- "..." (More actions) in the header of each open message opens its menu, like right-click. The message window has its
  own bar: Delete, Archive, Move to, Read/Unread, Flag, Print and More actions.
- Save as (an .eml or PDF file), Print, the message source with all its headers (for messages with very large
  attachments the first 2 MB, which are enough for the headers), messages from the same sender.
- Right-click menu on folders as in Outlook. Empty folder moves the folder's messages to Deleted Items (from Deleted
  Items and Spam it deletes them permanently, after a confirmation); as on the Gmail website it isn't there for All
  Mail and Starred, which also contain the messages of the other folders.
- Calendar for Microsoft accounts (Outlook.com and Microsoft 365) and Google accounts (Gmail and Google Workspace),
  from the button in the left column: Day, Work week, Week and Month, the calendars of each account with their colors
  (you show and hide them), event details with attendees and responses, "Join" for online meetings. With more than
  three events in a day, "+N more" opens that day with all of them. The first time "Allow access to calendar" opens the
  Microsoft window or, for Google, the browser (only when you select it). Reminders arrive as Windows notifications
  while Missiva is open (also in the notification area); those due while the PC was asleep or offline arrive late as
  soon as Missiva reads the calendar again, but only for meetings not yet over, and the notification says so ("Late
  reminder: it was due at 8:45 AM"); those for meetings already over don't arrive. New event (Ctrl+N in the calendar,
  Ctrl+Shift+A from anywhere), or a double-click on a time or a day: title, attendees (suggested from the address
  book: with attendees it becomes a meeting and the invitations go out), calendar, start and end or all day, repeat
  (every day, every week on the chosen days, every month), location, reminder, show as, description; a Teams meeting
  for work accounts. You can also reply to the events you're invited to from the details: Accept, Tentative, Decline.
  Your own events can be edited (double-click, or Edit in the details; with attendees "Send update"), moved by dragging
  them to another time, deleted or, if they're meetings, canceled with a note for the attendees (for Google without a
  note: Google Calendar doesn't send it, and Missiva says so); for recurring ones you choose this event only or the
  whole series.
  From the message menu "Reply with meeting" prepares a meeting with the sender and the other recipients. With work
  accounts "Check availability" shows when the attendees are free.
- People, like the new Outlook: your contacts with search and their card (addresses, phones, company, job title,
  address, notes, where you can also keep a PEC address and a VAT number), Send email, New contact, Edit, Delete. With
  Microsoft accounts they're your Outlook contacts and with Google accounts those in Google Contacts (the first time
  "Allow access to contacts", only when you select it; for Google they're read, and after "Allow editing contacts" you
  can also create, change and delete them from Missiva); for other accounts the contacts live in Missiva, on the PC,
  encrypted. Import a vCard or an Outlook or Gmail CSV (without photos; even thousands of contacts in under a second),
  Export to vCard.
- Other addresses to write from (aliases, "Send as"): in Manage account add a name, an address and, if needed, Reply
  to. They appear in the From field; replying to a message written to the alias goes out from the alias. Each one can
  have its own signature (Settings, Accounts, Signatures). For Gmail the address must also be added in Gmail (Settings,
  Accounts and Import, Send mail as); for Microsoft 365 you need the Send As permission given by your administrator.
- Offline, the messages you've already opened and the most recent Inbox messages downloaded in advance can still be
  read, from the copy on disk (encrypted, only for your Windows user), with the line "Offline: copy from ...". Under the
  account, "Offline" says why: the connection, or a firewall (like Portmaster) blocking Missiva. Drafts aren't copied
  (they're always resumed from the server); the copy of a deleted, moved or emptied message is removed, and after 30
  days it goes anyway. Certified email read from the copy says that the signature is checked only online.
  When you haven't used the window for half a minute (or Missiva is in the notification area), Missiva also keeps the
  Inbox messages of the last 30 days on the PC, like classic Outlook: it downloads them in the background, one at a
  time, without marking them as read (for Gmail and IMAP the whole message if it's under 300 KB; for larger ones, with
  attachments, only the text and the pictures in the text, if together they're under 2 MB), up to about 180 MB per
  account. Offline, search looks in this copy ("Results on this PC").
- Working offline: Delete, Archive, Move, Read/Unread and Flag done offline show right away and go into a queue (under
  the account "N pending actions"; Undo works as always while you're offline, and if meanwhile the connection came
  back and the move already went out, Missiva says so and puts back only the messages that hadn't gone out yet); when
  the connection comes back they go out once, and if meanwhile a message is gone (deleted from your phone, for example)
  Missiva says so; if a new sign-in is needed first, or the server is busy, they wait and Missiva says why. Send while
  offline puts the message in Missiva's Outbox: a bar at the bottom of the list says so, with Try again and Undo, and
  the message goes out by itself when the connection comes back, even with Missiva in the notification area. If
  instead the connection drops right while the server is receiving the message, it doesn't go into the Outbox (sending
  it again could make it arrive twice): a "Sending interrupted" window says it may have been sent, and you check. A
  message already in Deleted Items is deleted permanently only online.
- Missiva stays in the notification area, next to the clock, when you close the window (like Outlook): it keeps
  checking mail, showing notifications, sending scheduled messages and bringing back snoozed ones, using little memory
  (the part that draws messages closes). A click on the icon reopens it right away; right-click, Exit to close it
  completely. In Settings, General, Startup and exit you choose whether to close it completely and whether to start it
  at Windows sign-in, hidden.
- Certified email (PEC, Italian registered email), like the providers' webmail: the provider's envelope opens by itself
  and the reading pane shows the real message with its attachments, under a bar that says who sent it, when and
  through which provider, and whether the provider's signature is verified (green: the certificate is the provider's,
  for mail, and Windows recognizes it), intact but with a certificate Windows doesn't know (yellow, the everyday case)
  or altered (red). An envelope without a provider's signature, or signed with another certificate, is red: "This
  looks like certified email, but...". Acceptance and delivery receipts read as one line; failed deliveries are in red
  with the reason; the anomaly (a non-certified message that arrived in the PEC mailbox) is yellow, with the original
  attached. Reply and "Show images" apply to the real sender, not to the provider. Signed messages (S/MIME) also say
  who signed them, and are red if the certificate isn't the sender's.
- Signed .p7m files (Italian e-invoices, digitally signed documents): a click opens them in Missiva with the signer,
  the date and the state of the signature; if there's more than one signature, all signers are listed, and a single
  bad signature (an expired certificate, or one not suited to signing documents) is enough for the bar to say so, with
  the name of whoever added it; invoices (XML) read as text, other documents are saved or opened with their program.
- Attachment preview in the reading pane, like the new Outlook: a click on a PDF, an image (PNG, JPEG, GIF, BMP) or a
  text file (.txt, .csv, .xml, .json, .log), on the card in the pane, on the chip in the list or on a file in the
  search suggestions, shows it in place of the message, with zoom, arrows for the other attachments, Download, Open
  and Back to message (also Esc). PDFs and images are drawn by Windows in a separate, protected process, as in
  browsers: with no network and no access to your files (settings, sign-ins, mail), it can't start programs and it
  closes by itself after a minute; no JavaScript, links or forms from the PDF. No temporary files. Office documents
  still open with their program.
- All accounts, as in Thunderbird: with two or more accounts, at the top of the folders, Inbox, Unread, Flagged, Drafts
  and Sent Items of all accounts together, in date order. Each row has the color and name of its account (and the
  reading pane repeats it under the subject); Reply goes out from that account, Delete and Archive use each account's
  Deleted Items and Archive, Undo included.
- Search in all folders of the open account, only in the open folder or in all accounts (the menu next to the box;
  from the all-accounts views it always searches all of them); each result shows its folder (and its account) and the
  next ones arrive as you scroll. For Gmail with the same syntax as Gmail's search, which like Gmail doesn't look in
  Spam and Trash. As you type, like the new Outlook, the panel opens with the All, Mail, People and Files tabs: recent
  searches, people you write with, attachments by name and messages. Arrows and Enter to choose: a person shows the
  messages from and to them, a file or a message opens among the results.
  Filters, like the new Outlook (the funnel next to "Where to search"): From, To, Subject, Keywords, Date (from one
  day to another), Has attachments, Unread and Flagged, also in all accounts. The chosen filters become chips in the box,
  each with an x to remove it; you can also type them: from:rossi, to:"Mario Rossi", subject:quote, after:2026-03-01,
  before:2026-03-31, has:attachment, is:unread, is:flagged (and the Italian ones: see "Language"). They stay in recent
  searches. Dates apply in your PC's time, even on the day the clocks change.
- New messages right away, like Thunderbird: with Gmail and the other IMAP accounts the server tells Missiva as soon as
  a message arrives (IDLE), and likewise for messages read or moved from your phone; with Microsoft accounts Missiva
  asks for the Inbox changes every 30 seconds (messages read and deleted elsewhere update in the list). Also with
  Missiva in the notification area. The backup check stays for all: every minute with Gmail and the other IMAP accounts,
  every two minutes with Microsoft accounts. When the PC resumes from sleep and when the network changes, Missiva redoes
  its connections right away and checks all accounts.
- Windows notifications for new messages, also while Missiva is in the notification area: a click on a message's
  notification opens it in its own window (from there a button brings it into the reading pane of the main window); a
  click on "N new messages" opens that account's Inbox. A message's notification has three buttons: Reply (opens the
  reply in a compose window, where you see who it's going to), Flag (flags it) and Delete (to Deleted Items); in the
  "..." menu Snooze for 1 hour and "Snooze...", with the day and time to choose. Flag, Delete and Snooze for 1 hour
  work without opening Missiva. You turn them off per account, and the sound for all of them, in Settings, General,
  Notifications. On the taskbar icon there's a number, your choice in Settings, General, Notifications, "Number on the
  Missiva icon": the unread messages in the Inboxes (the default), the new messages that arrived since you last looked
  at Missiva (those of the notifications, even with notifications off; back to zero when you open the window) or no
  number, and for which accounts ("Count these accounts"). The tooltip of the icon next to the clock says the same
  number.
- Downloads, as in a browser: opening an attachment with a program, saving it (Save as, Save all: one per file),
  saving a message as .eml or the document of a signed file puts an item in the downloads, with the icon in the title
  bar next to the update button: while downloading a ring shows how far along it is (with more than one file, how many
  there are), when it's done a check mark appears for a few seconds, and an error stays marked until you look at it.
  A click opens the list for this session (the last 20), with the state of each one (in progress with the percentage,
  done, failed with the reason, canceled), Cancel for those in progress (no half file is left behind), Open and Show in
  folder for those done (Open asks the same questions as the reading pane), and "Clear list". The preview in the
  reading pane doesn't go through it: there you already see it loading.
- Shortcuts like the new Outlook: Ctrl+N new (a message in mail, an event in the calendar; from anywhere Ctrl+Shift+M
  a message and Ctrl+Shift+A an event), Ctrl+R reply, Ctrl+Shift+R reply all, Ctrl+F (or Ctrl+Shift+F) forward,
  Ctrl+Shift+V move to a folder, Ctrl+Z undo, Ctrl+. and Ctrl+, next and previous message, Ctrl+Q and Ctrl+U read and
  unread, Insert flag, Ctrl+P print, Ctrl+E search, Delete delete, Enter open in a window, F5 sync. While writing
  Ctrl+Enter sends, Esc closes like Discard, Ctrl+K inserts a link, Ctrl+S saves the draft.
- Light or dark theme like Windows, also when it changes with Missiva open (for example the automatic switch at
  sunset): the open message is redrawn staying where it was. Or always light or always dark (Settings, General,
  Appearance).

## Security

- No code in emails ever runs: JavaScript is off in the reading pane, and scripts, frames and forms are removed from
  the email even before showing it. Also when you reply or forward from a Gmail or IMAP account (with Microsoft
  accounts the Outlook server prepares the quote): the quoted message goes out without scripts, forms and links that
  run something, and with its images as they were.
- The pane never connects to the Internet by itself: every request goes through Missiva, which lets through only the
  images of the senders for whom you selected Show images. This way the sender doesn't know if and when you read.
- Outside the pane, the only request Missiva makes by itself toward a sender is the one for their logo (see "Sender
  logos" in What Missiva does; a DNS lookup, then the logo from their site): it goes out when the list shows one of
  their messages, normally once every 30 days per company, and doesn't say which message or whether you opened it; the
  site does see the IP address and the time. With the logos check box off it no longer goes out.
- A Missiva installed from a release asks GitHub whether there's a new version two minutes after startup and then once
  a day (GitHub sees the IP address, the time and the name "Missiva"; you turn it off in Settings, General, About), and
  accepts an update only if it's signed with the key of whoever publishes Missiva, newer, with the right hash and only
  the expected files ([docs/aggiornamenti.en.md](docs/aggiornamenti.en.md)).
- Links open in your browser, never inside Missiva.
- Attachments saved or opened with the "downloaded from the Internet" mark: Office opens them in Protected View and
  SmartScreen checks programs. The file types that can start programs or connect to a server by themselves (the same
  ones Outlook blocks, like .exe, .js, .bat, .iso, .library-ms, .search-ms, .rdp) ask for confirmation before opening,
  and "Save all" skips them.
- Opened attachments are deleted from the temporary folder when Missiva closes and, while it's open, an hour after
  being opened or last saved (a file still open in another program stays, and is removed at a later start, when it's
  more than a day old).
- Suspicious links: if the text shows a site and the link leads elsewhere, or if the address uses characters that
  imitate those of a well-known site, a warning with the real address appears before opening it.

## What's still missing

- Calendar: today's agenda next to the mail; the calendar of other accounts (CalDAV) and of shared mailboxes. Moving
  a whole series changes only the time: the days of the repetition are changed in Outlook or Google Calendar on the
  web. Google's calendar doesn't have Meet meetings or the cancellation note.
- People: Google's "Other contacts" (the ones Gmail collects by itself); Outlook contact groups (Missiva's live on the
  PC); the person's card for recipients (it's there for the sender).
- Automatic replies: those of Gmail and other accounts (they're there for Microsoft accounts).
- Rules: those of Gmail and other accounts run only with Missiva open (the filters of the Gmail website would need one
  more permission on the Google Cloud project); Outlook rules don't have Flag among their actions, and their rarer
  conditions (headers, size, exceptions) show only as "other Outlook conditions", and stay as they were when you edit
  the rule. The rules of shared mailboxes are changed in Outlook on the web.
- Shared mailboxes: calendar, Outlook categories and automatic replies of the shared mailbox; Outlook invitations that
  arrive in the shared mailbox don't have the card with Accept and Decline.
- Offline: attachments aren't kept on the PC (only their names and the pictures in the text); for the Inbox the last
  30 days are kept, for other folders the opened messages. The Outbox lives in memory: if you exit Missiva (Exit, not
  the X) before the connection comes back, the unsent messages reopen so you can save them as drafts or discard them,
  and offline the draft can't be saved either. Copy, categories, Snooze and flag dates offline don't go into the
  queue.
- All accounts: filter, "Go to" and sort apply to the folders of single accounts; Move to, Copy, Categorize and Snooze
  work on the messages of one account at a time.
- IMAP accounts: conversations aren't grouped (the server doesn't know them); on servers without IDLE new messages
  arrive with the check every minute.
- Drafts resumed with Edit: the text returns to Missiva's editor with the formatting Missiva can write (font, colors,
  alignment, indent, lists, tables, links, pictures added in Missiva); rarer styles, pictures from the Internet and
  those of the quoted message are left out or become plain text. A reply resumed from Drafts goes out as a new message
  (outside the conversation).
- Pinned messages: they stay only in Missiva. Microsoft Graph doesn't expose Outlook's "pinned", so messages pinned in
  Outlook don't show as pinned here, and vice versa.
- Copilot summary: it's a Microsoft service, not something Missiva can reproduce.
- Microsoft accounts: attachments can total up to 150 MB per message, Outlook's limit (Missiva says so when you add the
  file). Those of 3 MB and up are uploaded in pieces: the line at the bottom of the compose window says how far the
  upload is, and once in the draft they're not uploaded again. If the connection drops halfway, the file isn't added
  and Missiva says so; closing the compose window stops the upload.

## Backup: moving to a new PC or putting everything back

What's yours in Missiva lives in a single file, to restore on this PC or on another one. Copying the data folder isn't
enough: it's encrypted for your Windows user, and can't be read anywhere else.

- **Making a backup**: Settings, General, Backup, **Export…**. Choose a password (at least 8 characters, typed twice)
  and where to save the file, which ends in `.missiva-copia`. The backup is encrypted with that password: without it,
  it can't be opened, and Missiva doesn't keep it and can't recover it. Write it down somewhere you'll find it.
- **What it contains**: settings, accounts (addresses, servers, the other From addresses), signatures, templates,
  Missiva rules, categories and their colors, Favorites and folder order, Missiva contacts and groups, the address book
  of suggested recipients, flags with their dates, snoozed messages, the senders whose images you show. At most 64 MB
  of data.
- **What it doesn't contain**: mail (it's on the servers, and downloads again), sender logos (on the new PC they come
  back as Missiva reads the mail again), scheduled sends of Gmail and other accounts (the drafts stay in Drafts:
  schedule them again), and the sign-ins to the accounts. Microsoft and Google sign-ins never go in. IMAP account
  passwords only if you turn on **Include the passwords of IMAP accounts**: it's off by default, because with that
  check box whoever has the file and its password gets into your mailboxes.
- **Restoring it**: on the new PC install Missiva and open it; the Add account window has "Do you have a Missiva
  backup made on another PC? **Import it**" (or Settings, General, Backup, **Import…**). Choose the file and type the
  password: Missiva tells you when the backup was made and what it contains: the accounts with their servers, the
  rules in words, the senders whose images are always shown. If there's something to look at before saying yes it
  puts it at the top: a rule that applies to all messages, an account that's already on that PC and has another
  server in the backup. If Missiva already has data on that PC it asks "Replace everything with this backup?":
  importing replaces everything, it doesn't merge. The old data isn't deleted: it stays in the data folder with
  `prima-dell-importazione` and the date in the name. It also holds the sign-ins of the old accounts: when you no
  longer need them, the same Settings page shows "Delete the old data…", which removes them for good after a question.
  The address book of recipients arrives with names and addresses but without the counts: the suggestions get back in
  order once Missiva has read Sent Items again.
  Then Missiva restarts (Restart now; with Later the import waits for the next start, and you can cancel it from the
  same page).
- **What has to be redone on the new PC**: signing in to each account. After the restart all accounts are there, and
  under each one there's a bar: **Sign in again** for Microsoft and Google, **Update password** for the IMAP accounts
  whose password you didn't include. Mail downloads again by itself. When you restore the backup on the same PC, the
  accounts that were already there keep their sign-in, if they have the same servers in the backup (for Microsoft and
  Google the same app). Missiva rules arrive turned on: the notice after the restart reminds you.
- Wrong password, incomplete file, damaged or modified file, a backup made with a newer Missiva, a backup containing
  something Missiva doesn't write (prepared or changed by someone else): Missiva says which case it is, and changes
  nothing. Import only backups you made yourself: whoever prepares a backup chooses the accounts, servers and rules
  inside it.

## Files

- Settings, accounts, tokens and IMAP account passwords (encrypted): `%LOCALAPPDATA%\Missiva`; the copy of the mail
  for startup and for reading offline, encrypted: `%LOCALAPPDATA%\Missiva\cache`. Also encrypted: the address book of
  suggestions, Missiva contacts (`contatti.dat`), the queue of actions done offline (`coda.dat`), templates, Missiva
  rules (names, addresses, words and folders), recent searches, and subject and sender of snoozed, scheduled and
  flagged messages. Removing an account also removes from the PC its flags, templates, snoozed messages, scheduled
  sends and signatures (the mail on the server stays).
- Next to `impostazioni.json` (the settings) there's `impostazioni.bak`, the previous save. If the settings file gets
  damaged (a power cut, the disk), at startup Missiva sets it aside with the date in the name
  (`impostazioni-illeggibile-...json`, never deleted or overwritten), starts again from the backup copy and says so,
  with where the files are. The same goes for `contatti.dat` and `coda.dat` when they can't be read. The data is
  encrypted for your Windows user: copied to another PC or another profile, passwords and sign-ins have to be redone
  (the bar under the account says so) and the encrypted texts can't be read; in the file they stay as they were. To
  move to a new PC there's the backup (above).
- Error log: `%LOCALAPPDATA%\Missiva\missiva.log` (also from Settings, General, About, Open the logs folder; Settings
  open with the gear at the bottom of the left column), without message subjects or event titles: you can send it to
  whoever helps you.

## Building

You need the .NET 10 SDK: `dotnet build` builds the test version, which starts with `strumenti\prova.ps1` on a copy of
the data (started by itself, without `MISSIVA_DATA`, it closes right away: it would work on the real data); to install,
`installa.ps1`, with PowerShell 7 (see above). Built this way Missiva doesn't check for updates: only a release
(`-p:MissivaAggiornamenti=owner/repository` and a public key in `Core/UpdateKey.cs`) does.
