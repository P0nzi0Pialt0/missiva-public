#if DEBUG
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Missiva.UI;

namespace Missiva;

/// <summary>
/// Le prove di Persone con le azioni (1 ottobre 2026): numeri da chiamare (normalizzazione e validazione, anche numeri strani),
/// collegamenti tel: e Google Contatti (solo indirizzi costruiti da Missiva, id con caratteri strani rifiutati), niente apertura
/// vera (OpenExternal e CopyText di prova scrivono solo nel registro), sito web, e l'elenco a pixel con le sole righe in vista.
/// </summary>
static partial class ProvaBozze
{
    static void PeopleActionsTests()
    {
        Report.Add("--- Persone: chiamare, Google Contatti, sito, elenco a pixel");

        // Numeri: cifre e un "+" iniziale, il resto che separa si scarta; lettere e segni di altro tipo, troppo corti o troppo lunghi: niente collegamento.
        var good = new (string Text, string Number)[]
        {
            ("+39 333 1234567", "+393331234567"), ("333-123.4567", "3331234567"), ("(051) 000 0005", "0510000005"), ("0039 333 1234567", "00393331234567"),
            ("  347 000 0002 ", "3470000002"), ("051/000.0004", "0510000004"), ("3\u00A0333\u00A01234", "33331234"), ("123", "123"), ("+123", "+123"),
            (new string('7', 20), new string('7', 20)), ("+" + new string('7', 20), "+" + new string('7', 20)),
        };
        Check(good.All(g => ContactLinks.NormalizePhone(g.Text) == g.Number && ContactLinks.TelUri(g.Text) == "tel:" + g.Number),
              "numeri normalizzati: " + string.Join(" | ", good.Take(4).Select(g => $"{g.Text.Trim()} diventa {g.Number}")));
        var bad = new string?[]
        {
            null, "", "   ", "12", "+12", "+", "333 abc", "int. 12", "1-800-FIORI", "*21#", "#31#3331234567", "+39+333", "++39333", "333;rm", "333,4444", "tel:+393331234567",
            new string('7', 21), "+" + new string('7', 21), "\u0663\u0663\u0663", "333\n1234567", "333\t1234567x", "<script>", "333 1234567 int. 4", "+39 333 1234567 ext 12",
        };
        Check(bad.All(b => ContactLinks.NormalizePhone(b) == null && ContactLinks.TelUri(b) == null), $"numeri non validi, nessun collegamento ({bad.Length}: lettere, *21#, troppo corti e troppo lunghi, cifre non ASCII, a capo)");

        // Google Contatti: un solo indirizzo https, dell'utente solo l'id validato.
        Check(ContactLinks.GoogleContactsUrl("people/c123") == "https://contacts.google.com/person/c123"
              && ContactLinks.GoogleContactsUrl("people/c8461329287347982-1") == "https://contacts.google.com/person/c8461329287347982-1",
              "Google Contatti: people/c123 apre https://contacts.google.com/person/c123");
        var oddIds = new string?[]
        {
            null, "", "people/", "people", "c123", "people/c123/../x", "people/c12 3", "people/c123?x=1", "people/c123#a", "people/c123\n", "people/c123\r\nHost: evil", "people/c\u00E8",
            "people/c123;", "people/%63123", "people/c123/", "https://evil.example/people/c1", "people/" + new string('1', 65), "../people/c1", "people/c1&q=2", "people\\c1",
        };
        Check(oddIds.All(i => ContactLinks.GoogleContactsUrl(i) == "https://contacts.google.com/"), $"Google Contatti: senza id o con un id strano ({oddIds.Length} casi) solo la pagina di Google Contatti, nessun testo del contatto");

        // Cosa PeopleView.OpenExternal aprirebbe: solo tel: con cifre, Google Contatti, siti http e https.
        var allowed = new[]
        {
            "tel:+393331234567", "tel:333", "https://contacts.google.com/", "https://contacts.google.com/person/c123", "https://contacts.google.com/person/c123?authuser=ale%40gmail.com", "https://contacts.google.com/?authuser=a.b%2Bc%40x.example", "https://www.esempio.it/", "http://sito.example/pagina?x=1",
        };
        var refused = new string?[]
        {
            null, "", "tel:", "tel:12", "tel:333;ext=1", "tel:333\n", "tel:+39 333", "tel:*21#", "mailto:a@b.example", "javascript:alert(1)", "file:///C:/Windows/System32/calc.exe", "calc.exe",
            "ms-settings:", "ftp://sito.example/", "https://utente:pw@sito.example/", "https://localhost/", "https://contacts.google.com/person/c123/../x/y", "https://sito.example/a b", "C:\\Windows\\notepad.exe",
            "https://contacts.google.com/person/c123\n", "https://contacts.google.com/person/c123?authuser=a b",
        };
        Check(allowed.All(ContactLinks.IsAllowed) && refused.All(r => !ContactLinks.IsAllowed(r)), $"IsAllowed: {allowed.Length} indirizzi sì, {refused.Length} no (altri schemi, argomenti, percorsi, utente e password)");
        Check(ContactLinks.WebUrl("www.esempio.it") == "https://www.esempio.it/" && ContactLinks.WebUrl("https://a.example/p?q=1") == "https://a.example/p?q=1"
              && ContactLinks.WebUrl("ftp://a.example") == null && ContactLinks.WebUrl("javascript:alert(1)") == null && ContactLinks.WebUrl("a b.example") == null && ContactLinks.WebUrl("senzapunto") == null,
              "sito web: www.esempio.it diventa https, altri schemi e testo che non è un sito niente collegamento");

        // Modifica di un contatto di Google: l'indirizzo giusto; per gli altri (nessun provider) niente.
        var account = new MailAccount { Kind = AccountKind.Gmail, Email = "prova@gmail.com" };
        using (var google = new GmailProvider(account))
        {
            var person = new Contact { ServerId = "people/c555" };
            Check(PeopleView.GoogleEditUrl(person, google) == "https://contacts.google.com/person/c555?authuser=prova%40gmail.com" && PeopleView.GoogleEditUrl(person, null) == null,
                  "Modifica: un contatto di Google apre la sua scheda in Google Contatti, uno di Missiva no");
            Check(PeopleView.GoogleEditUrl(new Contact { ServerId = "people/c1/../../x\n" }, google) == "https://contacts.google.com/?authuser=prova%40gmail.com" && PeopleView.GoogleEditUrl(new Contact(), google) == "https://contacts.google.com/?authuser=prova%40gmail.com",
                  "Modifica: un id strano o assente apre solo Google Contatti");
            var weirdMails = new string?[] { null, "", "senza-chiocciola", "a b@x.example", "a&b@x.example", "a?b@x.example", "a#b@x.example", "a@x.example\n", "a@x.example/../y", "a@@x.example", new string('a', 65) + "@x.example", "a@" + new string('b', 256) };
            Check(ContactLinks.GoogleContactsUrl("people/c123", "a.b+c@x.example") == "https://contacts.google.com/person/c123?authuser=a.b%2Bc%40x.example"
                  && weirdMails.All(m => ContactLinks.GoogleContactsUrl("people/c123", m) == "https://contacts.google.com/person/c123"),
                  $"Google Contatti: l'account giusto con ?authuser= (indirizzo semplice e codificato), {weirdMails.Length} indirizzi strani niente parametro");
        }

        // Quale telefono chiama Chiama: il primo valido (cellulare, ufficio, casa, gli altri di Outlook); i non numeri restano testo.
        var phones = PeopleView.PhonesOf(new Contact { Mobile = "333 abc", BusinessPhone = "051 000 0001", MoreBusinessPhones = ["051 000 0002"], HomePhone = " ", MoreHomePhones = ["06 000 0006"] });
        Check(phones.Select(p => p.Text).SequenceEqual(["333 abc", "051 000 0001", "051 000 0002", "06 000 0006"]) && phones.First(p => ContactLinks.TelUri(p.Text) != null).Text == "051 000 0001",
              "telefoni della scheda in ordine, Chiama sceglie il primo valido (non '333 abc')");

        // Nessuna apertura vera: nella prova (MISSIVA_NO_READ) i gestori di serie scrivono solo nel registro, e gli indirizzi non consentiti non si aprono.
        var before = Environment.GetEnvironmentVariable("MISSIVA_NO_READ");
        Environment.SetEnvironmentVariable("MISSIVA_NO_READ", "1");
        try
        {
            PeopleView.OpenExternal("tel:+393331234567");
            PeopleView.OpenExternal("https://contacts.google.com/person/c123");
            PeopleView.OpenExternal("calc.exe");
            PeopleView.OpenExternal("tel:333;rm");
            PeopleView.CopyText("mario.rossi@example.com");
            var log = File.ReadLines(Path.Combine(Store.Dir, "missiva.log")).TakeLast(8).ToList();
            Check(log.Any(l => l.Contains("prova: chiamerei +393331234567")) && log.Any(l => l.Contains("prova: aprirei Google Contatti: https://contacts.google.com/person/c123"))
                  && log.Count(l => l.Contains("persone: indirizzo non aperto")) == 2 && log.Any(l => l.Contains("prova: copierei negli appunti 23 caratteri")) && !log.Any(l => l.Contains("calc.exe") || l.Contains("333;rm")),
                  "nella prova chiamare, Google Contatti e Copia scrivono solo nel registro; 'calc.exe' e 'tel:333;rm' non si aprono");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MISSIVA_NO_READ", before);
        }

        // Il sito arriva da Google (urls), da Outlook (businessHomePage) e dalle vCard (URL), e torna nelle vCard.
        var person1 = GoogleContacts.FromPerson(JsonNode.Parse("""{"resourceName":"people/c9","names":[{"displayName":"Anna Bianchi"}],"urls":[{"value":"https://anna.example/"}]}""")!, "acc");
        var viaGraph = ContactFiles.FromGraph(JsonNode.Parse("""{"id":"g1","displayName":"Luca Verdi","businessHomePage":"www.luca.example"}""")!, "acc");
        var viaVCard = ContactFiles.VCard("BEGIN:VCARD\r\nVERSION:3.0\r\nFN:Carla Neri\r\nURL:https://carla.example/\r\nEND:VCARD\r\n");
        var again = ContactFiles.VCard(ContactFiles.WriteVCard(viaVCard));
        Check(person1.Website == "https://anna.example/" && person1.ServerId == "people/c9" && viaGraph.Website == "www.luca.example" && viaVCard[0].Website == "https://carla.example/" && again[0].Website == "https://carla.example/"
              && viaVCard[0].Clone().Website == "https://carla.example/" && GoogleContacts.PersonFields.Contains("urls") && ContactFiles.GraphFields.Contains("businessHomePage"),
              "sito web dei contatti: Google urls, Outlook businessHomePage, vCard URL (anche riscritta) e copia");

        // L'elenco: a pixel, con le righe create solo per quelle in vista (4000 contatti).
        var list = new ListBox { Width = 340, Height = 500 };
        PeopleView.SetUpList(list);
        // Senza le risorse dell app (qui non ci sono) un modello minimo: ScrollViewer con ItemsPresenter, righe da 20 pixel.
        list.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" TargetType=\"ListBox\"><ScrollViewer Focusable=\"False\"><ItemsPresenter/></ScrollViewer></ControlTemplate>");
        list.ItemContainerStyle = (Style)System.Windows.Markup.XamlReader.Parse("<Style xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" TargetType=\"ListBoxItem\"><Setter Property=\"Height\" Value=\"20\"/><Setter Property=\"Template\"><Setter.Value><ControlTemplate TargetType=\"ListBoxItem\"><Border/></ControlTemplate></Setter.Value></Setter></Style>");        list.ItemsSource = Enumerable.Range(0, 4000).Select(i => "Persona " + i).ToList();
        list.ApplyTemplate();
        for (int pass = 0; pass < 3; pass++)
        {
            list.Measure(new Size(340, 500));
            list.Arrange(new Rect(0, 0, 340, 500));
            list.UpdateLayout();
        }
        static T? Find<T>(DependencyObject d) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            {
                var child = VisualTreeHelper.GetChild(d, i);
                if (child is T t) return t;
                if (Find<T>(child) is { } deeper) return deeper;
            }
            return null;
        }
        static int Rows(DependencyObject d)
        {
            int n = 0;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            {
                var child = VisualTreeHelper.GetChild(d, i);
                n += (child is ListBoxItem ? 1 : 0) + Rows(child);
            }
            return n;
        }
        var viewer = Find<ScrollViewer>(list);
        int made = Rows(list);
        Check(viewer != null && VirtualizingPanel.GetScrollUnit(list) == ScrollUnit.Pixel && viewer.CanContentScroll && made is > 5 and < 100,
              $"elenco delle persone: unità Pixel, virtualizzato ({made} righe create su 4000; ScrollViewer {viewer != null}, a contenuto {viewer?.CanContentScroll}, altezza {viewer?.ScrollableHeight:0}, vista {viewer?.ViewportHeight:0})");
        if (viewer != null)
        {
            viewer.ScrollToVerticalOffset(33.5);
            list.UpdateLayout();
            var fractional = viewer.VerticalOffset;
            viewer.ScrollToVerticalOffset(60);
            list.UpdateLayout();
            Check(fractional == 33.5 && viewer.VerticalOffset == 60 && viewer.ScrollableHeight > 4000 * 10 && Rows(list) < 100,
                  $"scorrimento a pixel: posizione 33,5 e 60 px (non righe), altezza scorribile {viewer.ScrollableHeight:0} px, righe create {Rows(list)}");
        }
    }
}
#endif
