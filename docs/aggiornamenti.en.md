# Updates

[Italiano](aggiornamenti.md)

If you installed Missiva from a release published on GitHub (the repository's Releases page), Missiva keeps itself
up to date: when a new version comes out it tells you, and it updates only when you ask it to.

## How it works

- **Only releases.** The script the author uses to publish a release writes into the program the repository updates
  come from. A Missiva built from source (`installa.ps1` from the source folder) doesn't have it: it asks nobody
  anything, and you update it by running `installa.ps1` again. This way people who build it themselves never receive
  someone else's programs.
- **The check.** Two minutes after startup, and then once a day while it stays open, Missiva asks GitHub which is the
  latest release. If it's newer, it also downloads the manifest (`aggiornamento.json`) and its signature, two small
  files. In Settings, General, About there is the **Check for updates automatically** check box (on by default; when
  it's off, Missiva doesn't ask anything any more) and the **Check now** button.
- **The button.** When there's a new version, the **Update** button appears in the title bar, on the right before the
  window buttons, in the accent color. Selecting it opens a small panel with "Missiva 0.3.N" and its date, what's new,
  **Update** and **Later**: with Later, the button comes back the next day for that version. No window opens by
  itself, and nothing downloads by itself.
- **Update.** Missiva downloads the release zip into its data folder (progress shows in the button, with a thin bar
  and the percentage, and in the panel, with Cancel), checks it, tries the new program and asks for confirmation. Then
  it closes as with Exit (messages you're writing ask whether to save the draft; if you cancel, Missiva stays as it
  is), and the new program takes the place of the one that was running and reopens Missiva. If something goes wrong
  before the swap, the previous version stays and the button says "Update failed" (also at the next start, if the
  problem happened there): the panel says why.

## What Missiva checks before accepting an update

- The manifest's signature, made with the author's release key (ECDSA P-256 over SHA-256): Missiva carries the public
  half of the key in its code (`Core/UpdateKey.cs`) and accepts only a manifest signed with the private half.
- That the manifest is Missiva's and states a version higher than the installed one: never the same or an older one.
- That the downloaded zip has the size and the SHA-256 hash written in the manifest.
- That the zip contains only `Missiva.exe`, `installa.ps1`, `disinstalla.ps1`, `LICENSE` and `TERZE-PARTI.md`, with no
  folders and nothing else.
- That the extracted program states, in its file version, the version of the manifest (read from the file, without
  starting it). Then Missiva starts it once as a test, without windows, as `installa.ps1` does: if it doesn't start,
  Missiva doesn't close.
- The network: https only, and only to GitHub (`api.github.com`, `github.com` and the hosts where GitHub keeps release
  files, `objects.githubusercontent.com` and `release-assets.githubusercontent.com`), even when GitHub redirects to
  another address; every response has a size limit.

Someone who broke into the author's GitHub account, without the release key, could publish a release but couldn't
get any Missiva to accept it.

## What GitHub sees

One request two minutes after startup and then one a day: the IP address, the time and the program name ("Missiva").
No account, no mail data, no PC identifier. With Update, also the download of the zip. With the check box off, no
requests until you select Check now.

## The limits

- Whoever has the release key and its password can publish an update that Missiva accepts. The key lives on the
  author's PC, encrypted with a password; if it were lost, installed copies of Missiva would no longer accept updates
  and would have to be reinstalled by hand from a new release.
- Missiva doesn't have a Windows code signing certificate. On the first install from a downloaded file Windows warns
  ("Windows protected your PC": More info, Run anyway). Updates are downloaded by Missiva itself, and Windows doesn't
  mark them as downloaded from the Internet.
- With Smart App Control on, Windows may block an unsigned program. Missiva tries the new program before closing: if
  Windows doesn't let it start, the previous version stays and the button says so.
- The format of GitHub's responses and the file hosts are those of its documentation: the automated tests use a fake
  GitHub. If GitHub changed hosts, the check would stop with "address not allowed" in the log (`missiva.log`), without
  installing anything.

## For those publishing releases from a fork

You need your own key: the public half goes in `Core/UpdateKey.cs`, and the program must be built with
`-p:MissivaAggiornamenti=owner/repository` (the repository of the releases). Without that property, or with the key
empty, updates stay off.
