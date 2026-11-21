namespace Lotse.Core.Engine;

/// <summary>
/// Everyday nouns a B2 letter uses all the time but the vocabulary bank does not teach (it starts where these end):
/// Frau, Hotel, Gerät, Woche. The analyzer needs their gender for the agreement rules. Only nouns with one gender in
/// standard German are listed: der/das Laptop, der/das Teil, der/das Joghurt and the like are left out on purpose,
/// because a rule that calls a correct "das Laptop" wrong costs more trust than it earns. An entry in the bank wins
/// over the one here.
/// </summary>
internal static class CoreNouns
{
    // "article lemma plural" per entry; "-" for a noun without a plural.
    private const string List = """
        der Mann Männer; der Vater Väter; der Sohn Söhne; der Bruder Brüder; der Freund Freunde; der Chef Chefs;
        der Kunde Kunden; der Nachbar Nachbarn; der Lehrer Lehrer; der Arzt Ärzte; der Tag Tage; der Monat Monate;
        der Abend Abende; der Brief Briefe; der Vertrag Verträge; der Preis Preise; der Platz Plätze; der Raum Räume;
        der Balkon Balkone; der Garten Gärten; der Bildschirm Bildschirme; der Computer Computer; der Drucker Drucker;
        der Fehler Fehler; der Versuch Versuche; der Zug Züge; der Bus Busse; der Flug Flüge; der Urlaub Urlaube;
        der Job Jobs; der Beruf Berufe; der Antrag Anträge; der Vorschlag Vorschläge; der Wunsch Wünsche;
        der Grund Gründe; der Schaden Schäden; der Kühlschrank Kühlschränke; der Schlüssel Schlüssel;
        der Parkplatz Parkplätze; der Mieter Mieter; der Vermieter Vermieter; der Lehrgang Lehrgänge;
        der Inhalt Inhalte; der Unterricht -; der Verein Vereine; der Kaffee Kaffees; der Tisch Tische;
        der Stuhl Stühle; der Schrank Schränke; der Koffer Koffer; der Bahnhof Bahnhöfe; der Flughafen Flughäfen;
        der Weg Wege; der Arbeitgeber Arbeitgeber; der Arbeitsplatz Arbeitsplätze; der Trainer Trainer;
        der Beitrag Beiträge; der Kurs Kurse; der Termin Termine; der Anruf Anrufe; der Besuch Besuche; der Gruß Grüße;
        die Frau Frauen; die Mutter Mütter; die Tochter Töchter; die Schwester Schwestern; die Familie Familien;
        die Freundin Freundinnen; die Kollegin Kolleginnen; die Firma Firmen; die Arbeit Arbeiten; die Stelle Stellen;
        die Stadt Städte; die Straße Straßen; die Woche Wochen; die Stunde Stunden; die Minute Minuten;
        die Zeit Zeiten; die Antwort Antworten; die Frage Fragen; die Nachricht Nachrichten; die Miete Mieten;
        die Küche Küchen; die Tür Türen; die Schule Schulen; die Reise Reisen; die Ware Waren;
        die Tastatur Tastaturen; die Reparatur Reparaturen; die Sauna Saunen; die Gruppe Gruppen;
        die Klasse Klassen; die Sprache Sprachen; die Hilfe Hilfen; die Idee Ideen; die Liste Listen;
        die Seite Seiten; die Adresse Adressen; die Nummer Nummern; die Kasse Kassen; die Gebühr Gebühren;
        die Anzeige Anzeigen; die Uhr Uhren; die Ecke Ecken; die Praxis Praxen; die Bitte Bitten;
        die Kaution Kautionen; die Lösung Lösungen; die Erfahrung Erfahrungen;
        das Kind Kinder; das Hotel Hotels; das Zimmer Zimmer; das Haus Häuser; das Auto Autos; das Gerät Geräte;
        das Handy Handys; das Problem Probleme; das Angebot Angebote; das Paket Pakete; das Geld -; das Jahr Jahre;
        das Wochenende Wochenenden; das Büro Büros; das Projekt Projekte; das Team Teams; das Ticket Tickets;
        das Studio Studios; das Restaurant Restaurants; das Frühstück Frühstücke; das Internet -;
        das Fenster Fenster; das Bett Betten; das Bad Bäder; das Formular Formulare; das Gespräch Gespräche;
        das Thema Themen; das Wort Wörter; das Buch Bücher; das Programm Programme; das Konto Konten;
        das Ziel Ziele; das Ergebnis Ergebnisse; das Unternehmen Unternehmen; das Licht Lichter;
        das Fahrrad Fahrräder; das Training Trainings; das Treffen Treffen
        """;

    public static IEnumerable<(string Article, string Lemma, string? Plural)> All =>
        List.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => e.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Select(p => (p[0], p[1], p[2] == "-" ? null : p[2]));
}
