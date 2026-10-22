# Gmail: register your own app on Google Cloud

[Italiano](gmail.md)

With Gmail, signing in works like in the new Outlook: the browser opens, you choose your Google account and allow
Missiva to use your mail. But Google allows this only for apps registered on Google Cloud, and a single registration
inside Missiva, valid for everyone, would first have to pass the verification Google requires from apps that read
mail. Missiva hasn't done it. So each person registers their own "app": it's free, you do it once in about ten
minutes, and it works for all your Gmail accounts.

What you register is yours: the ID and the secret stay on your PC and Missiva talks directly to Google, with no
server in between.

## The steps

1. **Create a project.** Open https://console.cloud.google.com/projectcreate with your Google account and create a
   project named Missiva.
2. **Enable the Gmail API** in the project: https://console.cloud.google.com/apis/library/gmail.googleapis.com, then
   Enable.
3. **Describe the app.** Open https://console.cloud.google.com/auth/overview and select Get started. App name:
   Missiva. Support email: yours. Audience: External. Contact email: yours. Agree and create.
4. **Publish the app.** In Audience select Publish app and confirm. Don't skip this step: if the app stays "In
   testing", Google expires the sign-in every 7 days.
5. **Give the app the mail permission.** In Data access select Add or remove scopes and check
   `https://mail.google.com/`. If it isn't in the list, paste it at the bottom, under Manually add scopes. Then Update
   and Save.
6. **Create the client.** In Clients select Create client. Application type: Desktop app. Name: Missiva. Create. Copy
   the **Client ID** and **Client secret** right away, or download the JSON file: the secret is shown only at that
   moment. The Client ID ends with `.apps.googleusercontent.com`.
7. **Put them in Missiva.** Add account, type your Gmail address (or choose Google), open "Missiva's Google app",
   paste the Client ID and the Client secret and select Sign in with Google.

## In the browser, the first time

Google warns that it hasn't verified the app: that's normal, because the app is yours and hasn't gone through
Google's verification. Select Advanced, then Go to Missiva, and allow access leaving the mail check box selected.

From the second Gmail account on there's nothing more to register: Missiva uses the same Client ID.

## Google Calendar and contacts (optional)

You add them later, when you need them, to the same project:

| For | Enable | Add the scope (as in step 5) | Then in Missiva |
|---|---|---|---|
| Calendar | [Google Calendar API](https://console.cloud.google.com/apis/library/calendar-json.googleapis.com) | `https://www.googleapis.com/auth/calendar` | Calendar, "Allow access to calendar" |
| Contacts, read only | [People API](https://console.cloud.google.com/apis/library/people.googleapis.com) | `https://www.googleapis.com/auth/contacts.readonly` | People, "Allow access to contacts" |
| Contacts, also to edit them | the same | `https://www.googleapis.com/auth/contacts` | People, "Allow editing contacts" |

Every time you sign in again, leave all the check boxes selected on Google's page: if you clear one, that permission
is lost, and Missiva tells you.

## If something goes wrong

- **"Access blocked" with error 403 `access_denied`**: the app is still "In testing" and your account isn't among the
  test users. Publish the app (step 4).
- **After a week Missiva asks you to sign in again**: same cause, the app is "In testing". Publish it and then, in
  Missiva, Sign in again.
- **Missiva says the Client ID and Client secret are missing**: both must be pasted into the "Missiva's Google app"
  box of the Add account window.
- **Google Workspace, `admin_policy_enforced`**: the domain administrator blocks apps they haven't marked as trusted,
  or has turned IMAP off. Missiva says so and explains what to ask them: to allow the app with its Client ID in the
  Admin console, or to turn IMAP back on.

## What stays on the PC, and how to remove access

Missiva never sees your Google password. It keeps the sign-in token and the client secret encrypted with Windows data
protection, readable only by your Windows user. When you remove the account, Missiva asks Google to revoke the
authorization; if the request fails it tells you, and you can remove the authorization by hand at
https://myaccount.google.com/permissions.
