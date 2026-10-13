# Componenti di terzi

Missiva usa i componenti qui sotto, ognuno con la sua licenza. I sorgenti di Missiva non li contengono: li scarica
`dotnet` da NuGet quando si compila. L'eseguibile compilato invece li contiene, insieme a .NET: chi lo distribuisce
deve accompagnarlo con questo file e con `LICENSE`.

Elenco del 13 ottobre 2026. Le versioni dei componenti diretti sono in `Missiva.csproj`; quelle degli altri le sceglie
NuGet.

| Componente | A che cosa serve | Licenza | Titolare |
|---|---|---|---|
| AngleSharp | Leggere e ripulire l'HTML delle email | MIT | Copyright 2013-2026, AngleSharp |
| MailKit | IMAP e SMTP | MIT | .NET Foundation and Contributors |
| MimeKit (con MailKit) | Formato dei messaggi, firme | MIT | .NET Foundation and Contributors |
| BouncyCastle.Cryptography (con MimeKit) | Crittografia delle firme | MIT | Copyright (c) Legion of the Bouncy Castle Inc. 2000-2026 |
| Microsoft.Identity.Client | Accesso agli account Microsoft | MIT | (c) Microsoft Corporation |
| Microsoft.Identity.Client.Broker | Finestra account di Windows | MIT | (c) Microsoft Corporation |
| Microsoft.Identity.Client.Extensions.Msal | Conservazione cifrata degli accessi Microsoft | MIT | (c) Microsoft Corporation |
| Microsoft.IdentityModel.Abstractions (con Microsoft.Identity.Client) | Parti comuni dell'accesso | MIT | (c) Microsoft Corporation |
| Microsoft.Identity.Client.NativeInterop (con il Broker) | Finestra account di Windows, parte nativa | Microsoft Software License Terms | (c) Microsoft Corporation |
| Microsoft.Toolkit.Uwp.Notifications | Notifiche di Windows | MIT | (c) .NET Foundation and Contributors |
| Microsoft.Web.WebView2 | Riquadro di lettura dei messaggi | Licenza di WebView2 SDK (tipo BSD) | Copyright (C) Microsoft Corporation |
| SharpVectors.Wpf | Disegno dei loghi SVG dei mittenti | BSD-3-Clause | Copyright (c) 2010 - 2026, Elinam LLC |
| .NET 10 e Windows Desktop (WPF), dentro l'eseguibile | Il motore su cui Missiva gira | MIT | (c) .NET Foundation and Contributors |

Microsoft.Identity.Client.NativeInterop non è software libero: è un componente binario di Microsoft, che si può
distribuire alle condizioni del file `LICENSE` contenuto nel suo pacchetto NuGet. Missiva lo usa così com'è, senza
modificarlo.

.NET e WPF hanno a loro volta componenti di terzi, elencati nei loro archivi:
https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT e
https://github.com/dotnet/wpf/blob/main/THIRD-PARTY-NOTICES.TXT.

## Testi delle licenze

### MIT

Vale per i componenti indicati sopra come MIT, ognuno con il suo titolare.

```
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### SharpVectors (BSD-3-Clause)

Il testo che accompagna il pacchetto. I due componenti che contiene, Brotli e MinIoC, sono MIT: di loro qui sotto
c'è la nota di copyright, e il testo è quello riportato sopra.

```
BSD 3-Clause License

Copyright (c) 2010 - 2026, Elinam LLC
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

* Redistributions of source code must retain the above copyright notice, this
  list of conditions and the following disclaimer.

* Redistributions in binary form must reproduce the above copyright notice,
  this list of conditions and the following disclaimer in the documentation
  and/or other materials provided with the distribution.

* Neither the name of the copyright holder nor the names of its
  contributors may be used to endorse or promote products derived from
  this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

Brotli License (https://github.com/google/brotli)
Copyright (c) 2009, 2010, 2013-2016 by the Brotli Authors.
(MIT: il testo qui sopra)

MinIoC License (https://github.com/microsoft/MinIoC)
Copyright (c) Microsoft Corporation. All rights reserved.
(MIT: il testo qui sopra)
```

### Microsoft.Web.WebView2

```
Copyright (C) Microsoft Corporation. All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are
met:

   * Redistributions of source code must retain the above copyright
notice, this list of conditions and the following disclaimer.
   * Redistributions in binary form must reproduce the above
copyright notice, this list of conditions and the following disclaimer
in the documentation and/or other materials provided with the
distribution.
   * The name of Microsoft Corporation, or the names of its contributors
may not be used to endorse or promote products derived from this
software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```
