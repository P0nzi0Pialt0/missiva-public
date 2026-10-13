#if DEBUG
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Missiva.UI;

namespace Missiva;

/// <summary>
/// Persone divise per account (1 ottobre 2026, agente contatti-II): ordine delle sezioni (Missiva, poi gli account come in Settings.Accounts),
/// conteggi, intestazioni e note che non si scelgono, lo stesso contatto in più sezioni, ricerca che toglie le sezioni vuote e cambia i
/// conteggi, filtro per account, sezione senza contatti o senza consenso, e l'elenco piatto di 4000 persone e 5 sezioni con le sole righe in vista.
/// </summary>
static partial class ProvaBozze
{
    static Contact Person(string name, string email) => new() { DisplayName = name, Emails = [email] };

    static List<(PeopleView.Header Header, List<object> Items)> SectionsOf(List<object> entries)
    {
        var sections = new List<(PeopleView.Header Header, List<object> Items)>();
        foreach (var entry in entries)
            if (entry is PeopleView.Header header) sections.Add((header, []));
            else sections[^1].Items.Add(entry);
        return sections;
    }

    static string Names(IEnumerable<object> items) => string.Join(", ", items.OfType<PeopleView.Row>().Select(r => r.Contact.Name));

    static void PeopleSectionsTests()
    {
        Report.Add("--- Persone: elenco diviso per account");
        var accounts = Store.Settings.Accounts;
        var before = accounts.ToList();
        try
        {
            var a = new MailAccount { Kind = AccountKind.Gmail, Email = "ada.prova@gmail.com", GoogleScopes = GoogleAuth.ContactsWriteScope };
            var b = new MailAccount { Kind = AccountKind.Gmail, Email = "famiglia.prova.lunga@gmail.com", GoogleScopes = GoogleAuth.ContactsWriteScope };
            var c = new MailAccount { Kind = AccountKind.Microsoft, Email = "alex.prova@outlook.it" };
            var e = new MailAccount { Kind = AccountKind.Microsoft, Email = "vuoto@outlook.example" };
            accounts.Clear();
            accounts.AddRange([a, c, b, e]); // l'ordine della barra laterale
            using var ga = new GmailProvider(a);
            using var gb = new GmailProvider(b);
            var gc = new GraphProvider(c);
            var ge = new GraphProvider(e);
            var local = new List<Contact> { Person("Zeno Neri", "zeno@x.example"), Person("Anna Bianchi", "anna@x.example"), Person("Luca Verdi", "luca@x.example") };
            var remote = new Dictionary<IMailProvider, List<Contact>?>
            {
                [ga] = [Person("Segreteria studio", "segreteria@x.example"), Person("Anna Bianchi", "anna@x.example"), Person("Maria Rossi", "maria@x.example"), Person("Luca Verdi", "luca@x.example")],
                [gc] = null, // senza il consenso ai contatti
                [gb] = [Person("Paolo Gialli", "paolo@x.example"), Person("Anna Bianchi", "anna@x.example")],
                [ge] = [], // un account senza contatti
            };
            var errors = new Dictionary<IMailProvider, string>();
            var origins = PeopleView.Origins([ge, gb, ga, gc]); // in un altro ordine: conta Settings.Accounts
            Check(string.Join("|", origins.Select(o => o.Title)) == $"Contatti di Missiva|{a.Email}|{c.Email}|{b.Email}|{e.Email}",
                  "sezioni in ordine: Missiva, poi gli account come in Settings.Accounts (barra laterale), anche se i provider arrivano in un altro ordine");

            var all = PeopleView.BuildEntries(origins, local, remote, errors, "", "");
            var sections = SectionsOf(all);
            Check(sections.Count == 5 && string.Join("|", sections.Select(s => s.Header.Count)) == "3|4||2|0" && sections.Select(s => s.Header.First).SequenceEqual([true, false, false, false, false]),
                  $"intestazioni con i conteggi: {string.Join(" | ", sections.Select(s => $"{s.Header.Title} [{s.Header.Detail}] {s.Header.Count}"))}");
            Check(sections[0].Header.IsMissiva && sections[1].Header.Detail == "Google" && sections[2].Header.Detail == "Outlook" && sections.Skip(1).All(s => s.Header.Dot != null && !s.Header.IsMissiva)
                  && sections[0].Header.Dot == null && sections[1].Header.Dot != sections[3].Header.Dot,
                  "Missiva con l'icona della persona, gli account con il pallino del loro colore (diverso fra i due Gmail), tipo Google (scrivibile) e Outlook");
            Check(Names(sections[0].Items) == "Anna Bianchi, Luca Verdi, Zeno Neri" && Names(sections[1].Items) == "Anna Bianchi, Luca Verdi, Maria Rossi, Segreteria studio" && Names(sections[3].Items) == "Anna Bianchi, Paolo Gialli",
                  "dentro ogni sezione le persone sono in ordine di nome");
            var consent = sections[2].Items.OfType<PeopleView.SectionNote>().SingleOrDefault();
            var none = sections[4].Items.OfType<PeopleView.SectionNote>().SingleOrDefault();
            Check(consent != null && ReferenceEquals(consent.Allow, gc) && consent.Text.Contains("modificarli") && sections[2].Items.Count == 1
                  && none != null && none.Allow == null && none.Text == "Nessun contatto." && sections[0].Items.All(i => i is PeopleView.Row) && sections[1].Items.All(i => i is PeopleView.Row),
                  "account senza consenso: nota con il pulsante di consenso sotto la sua intestazione; account senza contatti: \"Nessun contatto.\"; le altre sezioni senza note");
            var anna = all.OfType<PeopleView.Row>().Where(r => r.Contact.Name == "Anna Bianchi").Select(r => r.Provider).ToList();
            Check(all.OfType<PeopleView.Row>().Count() == 9 && anna.Count == 3 && anna[0] == null && anna[1] == ga && anna[2] == gb,
                  "lo stesso contatto in più account sta in tutte le sezioni (Anna Bianchi in Missiva e in due account), nessuna unione: 9 righe");

            // Frecce: saltano intestazioni e note.
            int lastOfFirst = all.IndexOf(sections[0].Items[^1]);
            int firstOfSecond = all.IndexOf(sections[1].Items[0]);
            Check(firstOfSecond == lastOfFirst + 2 && PeopleView.ContactNear(all, lastOfFirst, 1) == firstOfSecond && PeopleView.ContactNear(all, firstOfSecond, -1) == lastOfFirst
                  && PeopleView.ContactNear(all, -1, 1) == 1 && PeopleView.ContactNear(all, all.Count, -1) == all.IndexOf(sections[3].Items[^1]) && PeopleView.ContactNear(all, 1, -1) == -1
                  && PeopleView.ContactNear(all, all.Count - 1, 1) == -1 && PeopleView.ContactNear(all, all.IndexOf(sections[1].Items[^1]), 1) == all.IndexOf(sections[3].Items[0]),
                  "giù e su saltano le intestazioni e le note (dalla sezione di Missiva alla seguente, e oltre la sezione senza contatti); Inizio e Fine sulle sole persone");

            // Ricerca: le sezioni senza risultati spariscono, il conteggio dice quanti ne hanno.
            var verdi = SectionsOf(PeopleView.BuildEntries(origins, local, remote, errors, "verdi", ""));
            Check(verdi.Count == 2 && verdi[0].Header.Title == "Contatti di Missiva" && verdi[0].Header.Count == "1 su 3" && verdi[1].Header.Title == a.Email && verdi[1].Header.Count == "1 su 4"
                  && verdi.All(s => s.Items.Count == 1 && s.Items[0] is PeopleView.Row) && verdi[0].Header.First,
                  "ricerca \"verdi\": restano solo Missiva (1 su 3) e il primo account (1 su 4); senza consenso, senza contatti e senza risultati spariscono, nessuna nota");
            var everyone = SectionsOf(PeopleView.BuildEntries(origins, local, remote, errors, "x.example", ""));
            Check(string.Join("|", everyone.Select(s => s.Header.Count)) == "3|4|2" && everyone.Count == 3,
                  "ricerca che trova tutti: i conteggi tornano semplici (3, 4, 2), senza \"su\"");
            Check(PeopleView.BuildEntries(origins, local, remote, errors, "zzz", "").Count == 0, "ricerca senza risultati: nessuna sezione");

            // Filtro per account.
            var onlyB = PeopleView.BuildEntries(origins, local, remote, errors, "", b.Id);
            var onlyC = PeopleView.BuildEntries(origins, local, remote, errors, "", c.Id);
            var onlyLocal = PeopleView.BuildEntries(origins, local, remote, errors, "", "missiva");
            Check(Names(onlyB) == "Anna Bianchi, Paolo Gialli" && SectionsOf(onlyB).Count == 1 && SectionsOf(onlyB)[0].Header.First && SectionsOf(onlyB)[0].Header.Title == b.Email
                  && onlyC.Count == 2 && onlyC[1] is PeopleView.SectionNote { Allow: not null } && Names(onlyLocal) == "Anna Bianchi, Luca Verdi, Zeno Neri" && SectionsOf(onlyLocal).Count == 1
                  && Names(PeopleView.BuildEntries(origins, local, remote, errors, "verdi", a.Id)) == "Luca Verdi" && PeopleView.BuildEntries(origins, local, remote, errors, "verdi", b.Id).Count == 0,
                  "filtro per account: un solo account (o Missiva) con la sua intestazione; senza consenso resta il pulsante; insieme alla ricerca si sommano");

            // Caricamento ed errore.
            var loading = new Dictionary<IMailProvider, List<Contact>?>(remote);
            loading.Remove(gb);
            var failing = new Dictionary<IMailProvider, string> { [ga] = "rete assente" };
            var waiting = SectionsOf(PeopleView.BuildEntries(origins, local, loading, failing, "", ""));
            Check(waiting[3].Header.Count == "" && waiting[3].Items.Count == 1 && waiting[3].Items[0] is PeopleView.SectionNote { Text: "Caricamento…" }
                  && waiting[1].Items[0] is PeopleView.SectionNote { Text: "Non disponibili (rete assente)" } && Names(waiting[1].Items) == "Anna Bianchi, Luca Verdi, Maria Rossi, Segreteria studio",
                  "account in caricamento: nota \"Caricamento…\"; errore: nota con il motivo e le persone di prima, se ci sono");

            // L'elenco piatto: 4000 persone in 5 sezioni, righe create solo per quelle in vista, con i due tipi di contenitore.
            var many = new Dictionary<IMailProvider, List<Contact>?>();
            var bigLocal = Enumerable.Range(0, 800).Select(i => Person($"Persona {i:0000} di Missiva", $"m{i}@x.example")).ToList();
            foreach (var p in new IMailProvider[] { ga, gc, gb, ge }) many[p] = Enumerable.Range(0, 800).Select(i => Person($"Persona {i:0000} di {p.Account.Email}", $"p{i}@x.example")).ToList();
            var big = PeopleView.BuildEntries(origins, bigLocal, many, errors, "", "");
            var list = new ListBox { Width = 340, Height = 500 };
            PeopleView.SetUpList(list);
            list.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" TargetType=\"ListBox\"><ScrollViewer Focusable=\"False\"><ItemsPresenter/></ScrollViewer></ControlTemplate>");
            var personStyle = (Style)System.Windows.Markup.XamlReader.Parse("<Style xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" TargetType=\"ListBoxItem\"><Setter Property=\"Height\" Value=\"20\"/><Setter Property=\"Template\"><Setter.Value><ControlTemplate TargetType=\"ListBoxItem\"><Border/></ControlTemplate></Setter.Value></Setter></Style>");
            PeopleView.SetUpEntries(list, personStyle);
            list.ItemsSource = big;
            list.ApplyTemplate();
            void Layout()
            {
                for (int pass = 0; pass < 3; pass++)
                {
                    list.Measure(new Size(340, 500));
                    list.Arrange(new Rect(0, 0, 340, 500));
                    list.UpdateLayout();
                }
            }
            Layout();
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
            static int Containers(DependencyObject d)
            {
                int n = 0;
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
                {
                    var child = VisualTreeHelper.GetChild(d, i);
                    n += (child is ListBoxItem ? 1 : 0) + Containers(child);
                }
                return n;
            }
            var viewer = Find<ScrollViewer>(list);
            int made = Containers(list);
            Check(big.Count == 4005 && viewer != null && VirtualizingPanel.GetScrollUnit(list) == ScrollUnit.Pixel && viewer.CanContentScroll && made is > 5 and < 100 && viewer.ScrollableHeight > 4000 * 10,
                  $"elenco piatto di 4000 persone e 5 sezioni (4005 righe con le intestazioni): a pixel, virtualizzato, {made} righe create (ScrollViewer {viewer != null}, a contenuto {viewer?.CanContentScroll}, altezza scorribile {viewer?.ScrollableHeight:0})");

            // Il contenitore di un'intestazione non si sceglie e non prende il fuoco, quello di una persona sì, anche con le righe riciclate fra i due tipi.
            int headerAt = big.IndexOf(big.OfType<PeopleView.Header>().ElementAt(2));
            bool Container(int index, bool header)
            {
                list.ScrollIntoView(big[index]);
                Layout();
                return list.ItemContainerGenerator.ContainerFromIndex(index) is ListBoxItem item && item.Focusable == !header && (item.Content is PeopleView.Header) == header;
            }
            bool first = Container(headerAt, true), second = Container(headerAt + 1, false), third = Container(2000, false), fourth = Container(headerAt, true), fifth = Container(big.Count - 1, false);
            Check(first && second && third && fourth && fifth && Containers(list) < 100,
                  $"intestazione non focalizzabile e persona sì, anche riciclando le righe (intestazione {first}, persona {second}, dopo uno scorrimento {third}, intestazione di nuovo {fourth}, ultima {fifth}); righe create {Containers(list)}");
        }
        finally
        {
            accounts.Clear();
            accounts.AddRange(before);
        }
    }
}
#endif
