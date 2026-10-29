# Serval — Obsidian
Projekt wizualny i specyfikacja frontendu • 23 października 2026

Status: propozycja do wdrożenia, bez zmian w aplikacji. Grafiki są makietami AI,
a nie zrzutami działającego frontendu. Dane usług są przykładowe.
Specyfikacja i tokens.css rozstrzygają nieścisłości grafiki: odcienie, rozmiary,
logo, stany i zachowania. To materiał projektowy poza repozytorium, nie nowy
zaakceptowany kontrakt OpenSpec.

## 1. Kierunek
Ciemna aplikacja narzędziowa, spokojna i czytelna. Granatowe tło, warstwowanie
powierzchni zamiast mocnych cieni, mięta wyłącznie do akcji, aktywnej nawigacji
i istotnych wyróżnień. Dekoracyjna architektura występuje wyłącznie przy logowaniu.
Widoki robocze przeznaczają przestrzeń na dane.

Pliki:
- 01-logowanie.png — ekran logowania.
- 02-lista-uslug.png — lista usług i fokus linku.
- 03-szczegoly-uslugi.png — szczegóły usługi.
- 04-komponenty.png — przykłady komponentów i stanów.
- tokens.css — proponowane zmienne CSS.
- prompts.json — briefy użyte z wbudowanym image_gen.
- index.html — galeria projektu.

## 2. Zgodność z obecnym frontendem
Źródła odczytane: ARCHITECTURE.md, SECURITY.md i widoki w src/Serval.Web/Pages.
Serval używa ASP.NET Core Razor Pages. Projekt nie wymaga Reacta ani zmiany stosu.

Istniejące dane:
| Pole | Etykieta projektu | Renderowanie |
| --- | --- | --- |
| Id | Usługa / Nazwa kanoniczna | pełna nazwa monospace, link na liście |
| Description | Opis | zwykły tekst, zawijanie |
| ActiveState | Stan | lokalizowana etykieta i symbol |
| SubState | Podstan | surowa wartość monospace |
| LoadState | Załadowanie | surowa wartość monospace |

Świadomie usunięto autostart, wybór hosta, awatar użytkownika, metryki,
zakładkę uprawnień pojedynczej usługi i akcje start/stop/restart:
obecne widoki/model nie dostarczają ich tak, jak sugerowały pierwsze makiety.
Nie tworzyć atrap funkcji. Link Uprawnienia wskazuje istniejącą stronę Grants;
jego prezentację powiązać z rzeczywistą polityką dostępu, bez zmiany jej reguł.

Nowe propozycje zachowania, wymagające osobnego uzgodnienia i implementacji:
- wyszukiwanie po Id i Description;
- filtrowanie po ActiveState i licznik wyników;
- jawne odświeżenie listy/szczegółów;
- przycisk pokazywania hasła;
- polska lokalizacja (obecne widoki są angielskie);
- responsywna nawigacja i komunikaty stanów.

Wyszukiwanie i filtry mogą działać lokalnie wyłącznie na już autoryzowanej liście,
bez zmiany backendowej autoryzacji. Nie wdrażać ich w ramach samego restylowania
bez uwzględnienia wymagań repozytorium dotyczących OpenSpec.

## 3. Kolory
| Token | HEX | Zastosowanie |
| --- | --- | --- |
| canvas | #111820 | tło aplikacji |
| sidebar | #141E28 | nawigacja boczna |
| surface | #1B2632 | panele i formularze |
| surface-hover | #223241 | hover wiersza/akcji |
| border | #344657 | subtelne separatory, nie jedyny sygnał interakcji |
| control-border | #65788C | rozpoznawalne obramowania pól |
| text | #F3F6FA | podstawowy tekst |
| text-secondary | #B6C2D1 | etykiety pomocnicze |
| accent | #79E2C0 | linki, CTA, fokus |
| accent-ink | #10251E | tekst na mięcie |
| danger | #FF9B9B | błąd |
| warning | #F2CE86 | przejściowy stan |
| neutral | #BCC8D6 | nieaktywność/nieznany stan |

Nie kodować znaczenia samym kolorem. Zawsze etykieta tekstowa;
symbol jest dodatkową pomocą, dekoracyjny dla czytnika ekranu.
Kontrast par tokenów sprawdzono obliczeniowo w contrast.json.
Nie jest to audyt WCAG całej aplikacji; raster może różnić się od tokenów.

## 4. Typografia i rytm
- Nagłówki: Space Grotesk, 600; awaryjnie system-ui, sans-serif.
- Treść i kontrolki: system-ui, sans-serif, 400/500/600.
- Identyfikatory: JetBrains Mono, 400/500; awaryjnie ui-monospace, monospace.
- Docelowo lokalne pliki WOFF2 z polskimi znakami i dołączonymi licencjami;
  bez zależności od zewnętrznego CDN. Fonty nie są zawarte w tym pakiecie.
- H1 desktop: 32/40 px; H1 mobile: 28/36 px.
- Nagłówek panelu: 20/28 px; treść: 16/24 px; dane tabeli: 14/22 px.
- Podpis pomocniczy: 14/20 px; unikać tekstów mniejszych niż 12 px.
- Skala odstępów: 4, 8, 12, 16, 24, 32, 48, 64 px.
- Kontrolki: minimum 44 px wysokości, formularz logowania 48 px.
- Radius: kontrolki 8 px, panel 12 px, etykieta statusu 999 px.
- Wiersz tabeli: minimum 64 px, wysokość rośnie przy długiej treści.

## 5. Szkielet aplikacji
Desktop >= 1024 px:
- Sidebar 232 px, pełna wysokość; marka u góry; Usługi i Uprawnienia poniżej.
- Wylogowanie przy dolnej krawędzi, jednak bez nakładania na nawigację przy zoomie.
- Górny pas 64 px: wyłącznie ścieżka bieżącego widoku.
- Obszar roboczy: padding 32 px, max-width 1440 px, wyrównanie do lewej.
- Jeden h1 na stronie. Bez zdublowanego menu u góry i z boku.

Tablet 768–1023 px:
- Sidebar zastępuje pasek marki i przycisk Menu.
- Menu jako rozwijany blok w przepływie dokumentu, bez modalnej pułapki fokusu.
- Padding treści 24 px, toolbar może przejść do dwóch rzędów.

Telefon < 768 px:
- Padding 16 px, h1 28 px, pola i główne przyciski na pełną szerokość.
- Logowanie bez dekoracyjnego panelu; zachowana marka i formularz.
- Lista: wszystkie kolumny pozostają w semantycznej tabeli w nazwanym,
  przewijanym poziomo regionie; przewija się wyłącznie tabela, nie cała strona.
- Instrukcja „Przewiń tabelę, aby zobaczyć pozostałe kolumny” gdy potrzebna.
- Szczegóły: etykieta nad wartością; nazwy zawijane, bez ucinania informacji.
- Sprawdzić przy 360 px oraz przy szerokości 320 CSS px i powiększeniu.

## 6. Logowanie
Układ desktop 46/54; formularz max-width 400 px. Ilustracja dekoracyjna,
tekst i logo renderowane jako osobne elementy HTML/SVG, nie raster strony.
Docelowy znak: dwie kanciaste miętowe uszy; jeden wspólny zasób we wszystkich
widokach. Makiety nie są finalnym plikiem logo ani zestawem assetów do produkcji.

Kolejność: h1, instrukcja, komunikat formularza jeśli obecny, użytkownik,
hasło, akcja główna. Pola mają trwałe etykiety, nie tylko placeholder.
Hasło początkowo puste — kropki na makiecie ilustrują wpisaną wartość.
autocomplete=username i current-password; nie blokować wklejania.
Pokazanie hasła: type=button, etykieta „Pokaż hasło” / „Ukryj hasło”,
aria-pressed, fokus zostaje na przycisku.

Stany:
- start: puste pola;
- brak wymaganej wartości: błąd pod polem i aria-describedby/aria-invalid;
- wysyłanie: „Logowanie…”, blokada ponownego submit, bez utraty danych;
- nieudane uwierzytelnienie: „Nie udało się zalogować. Sprawdź dane i spróbuj ponownie.”;
- niedostępność: odrębny komunikat ogólny tylko jeśli pozwala istniejący kontrakt błędów.

Nie ujawniać istnienia kont, szczegółów PAM ani powodów odmowy uprawnień.
Nie zmieniać obsługi sesji, CSRF, ciasteczek ani backendowej walidacji.
Brak rejestracji, social login i „zapamiętaj mnie”.

## 7. Lista usług
Nagłówek, opis, licznik, toolbar, tabela. Kolejność kolumn:
Usługa / Opis / Stan / Podstan / Załadowanie.
Id jest prawdziwym linkiem; nie uzależniać przejścia od kliknięcia całego wiersza.
Nazwa kanoniczna nie jest skracana; umożliwić zawinięcie długich identyfikatorów.

Proponowane filtry:
- wyszukiwanie bez rozróżniania wielkości liter po nazwie i opisie;
- łączenie wyszukania i filtra stanu przez AND;
- filtr „Wszystkie stany” oraz obecne na liście surowe stany z etykietami;
- licznik „Wyświetlono X z Y usług”;
- czyszczenie filtrów przywraca pełną już otrzymaną listę;
- zachować filtr po odświeżeniu i po powrocie ze szczegółów w obrębie sesji widoku;
- nie dodawać lokalnego sortowania, paginacji ani limitu wyników bez potrzeby.

Stany:
- brak dostępnych usług: „Brak usług do wyświetlenia.” — nie nazywać awarią;
- brak trafień: „Brak wyników.” i „Wyczyść filtry”;
- pobieranie: tekst „Pobieranie usług…” bez fałszywego licznika 0;
- błąd odczytu: „Lista usług jest chwilowo niedostępna.” i ponowienie;
- nie pokazywać danych poprzedniej sesji po błędzie lub utracie autoryzacji.

## 8. Szczegóły usługi
Ścieżka, link powrotny, nazwa, opis, etykieta stanu, Odśwież.
Dwa proste panele w jednej kolumnie: Stan usługi oraz Identyfikacja.
Nie ma zakładek, jeśli brak różnych rzeczywistych obszarów treści.
Stan: przyjazna etykieta obok surowego ActiveState; SubState i LoadState bez
swobodnego tłumaczenia. Identyfikacja: Id i Description.

Brak zasobu i odmowa dostępu zachowują obecne nierozróżnialne zachowanie 404;
makieta nie zmienia tego kontraktu. Błąd transportu jest komunikatem ogólnym.
Nie wymyślać czasu startu, wersji, metryk, uprawnień ani nazwy hosta.

## 9. Statusy i komponenty
| ActiveState | Etykieta | Symbol / kolor |
| --- | --- | --- |
| active | Aktywna | znacznik poprawności / mięta |
| inactive | Nieaktywna | pauza / neutralny |
| failed | Błąd | wykrzyknik / coral |
| activating | Aktywacja | obrót lub zegar / bursztyn |
| deactivating | Dezaktywacja | zegar / bursztyn |
| reloading | Przeładowanie | obrót / bursztyn |
| inne | surowa wartość | neutralny symbol |
| brak wartości | Nieznany stan | znak zapytania / neutralny |

To prezentacja, nie nowa interpretacja stanu w domenie. Nie traktować
nieaktywności jako błędu. Dodatkowe wartości zachować, nie mapować na active.

Komponenty do wydzielenia przy wdrożeniu:
AppShell, PageHeader, Breadcrumb, Button, FormField, StatusBadge,
ServiceTable, KeyValuePanel, EmptyState, InlineAlert.
Nazwy opisują odpowiedzialność; nie narzucają bibliotek ani Web Components.
Ikony: jeden zestaw liniowy 20 px / 1.75 px, bez mieszaniny emoji i ikon 3D.

## 10. Interakcja i dostępność
- Widoczny fokus: 2 px mięty, 3 px offset, bez usuwania outline.
- Aktywny link nawigacji aria-current=page i tło/wyróżnik obok koloru.
- Skip link „Przejdź do treści”; logiczna kolejność Tab.
- Natywne form, label, button, a, table, th scope=col, dl/dt/dd.
- Ikony dekoracyjne aria-hidden; ikony będące akcjami mają polskie nazwy.
- Błędy formularza powiązane z polami; podsumowanie błędów otrzymuje fokus po submit.
- Zmiany licznika/ładowania aria-live=polite; nie czytać każdej zmiany całej tabeli.
- Hover nie jest jedynym sposobem odkrywania kontroli.
- Zmiana koloru/tła 120–160 ms; bez pulsowania statusów.
- prefers-reduced-motion wyłącza nieistotny ruch.
- Docelowo WCAG 2.2 AA; potwierdzić na implementacji, nie na makietach.

Źródła:
https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum.html
https://www.w3.org/WAI/WCAG22/Understanding/use-of-color.html

## 11. Mapa wdrożenia i kryteria odbioru
- Pages/Shared/_Layout.cshtml: wspólny shell i semantyczna nawigacja.
- Pages/Login.cshtml: wariant bez sidebaru, etykiety, komunikaty.
- Pages/Services/Index.cshtml: tabela danych i uzgodnione filtry.
- Pages/Services/Details.cshtml: nagłówek oraz definicje stanu i identyfikacji.
- wwwroot/css/site.css: tokeny, komponenty, responsywność.
- wwwroot/js/site.js: wyłącznie uzgodnione ulepszenia zachowania.
- Uprawnienia, błędy i pozostałe strony muszą być czytelne we wspólnym shellu.

Przed wdrożeniem doprecyzować zakres OpenSpec dla zachowań i lokalizacji.
Nie modyfikować Agent/IPC/PAM dla dopasowania do grafiki.

Odbiór implementacji:
1. Logowanie, lista i szczegóły z prawdziwymi danymi.
2. Długie nazwy, brak opisu, nietypowe stany i brak wyników.
3. Klawiatura, czytnik ekranu, 200–400% zoom, 360/768/1024/1440 px.
4. Brak poziomego przewijania strony; tabela może przewijać się we własnym regionie.
5. Pomiar kontrastu rzeczywistych komponentów i wszystkich ich stanów.
6. Brak regresji CSRF, sesji, kodowania danych i polityki widoczności usług.
7. Porównanie renderu z projektem i niezależny review zgodnie z repozytorium.

## 12. Ograniczenia pakietu
Makiety wygenerowano wbudowanym image_gen. Mogą nie zachować idealnie geometrii
logo, fontu, odstępów i płaskich kolorów. Do implementacji obowiązują tokeny
i wartości z tego dokumentu. Plansza komponentów to przykłady, nie komplet
wszystkich stanów. Wariant mobilny opisano, nie przedstawiono osobną makietą.
Nie przeprowadzono testów działającego frontendu ani audytu dostępności.
