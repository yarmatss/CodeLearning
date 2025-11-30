## Moduł 1: Autentykacja i autoryzacja
### US-01: Rejestracja użytkownika
**Jako** niezarejestrowany użytkownik\
**Chcę** zarejestrować się w systemie\
**Aby** uzyskać dostęp do platformy

**Priorytet**: Must Have | **Punkty**: 3

**Kryteria akceptacji**:
-	Formularz: email, hasło, potwierdzenie hasła, imię, nazwisko
-	Wybór roli: Student / Nauczyciel
-	Walidacja: unikalność email, siła hasła (min. 8 znaków, wielka litera, cyfra)
-	Konto aktywne natychmiast (bez email confirmation)
-	Automatyczne logowanie po rejestracji (HttpOnly cookie)
-	Przekierowanie na dashboard dla roli
-	Komunikat sukcesu

### US-02: Logowanie użytkownika
****Jako**** zarejestrowany użytkownik\
**Chcę** zalogować się do systemu\
**Aby** korzystać z platformy

**Priorytet**: Must Have | **Punkty**: 5

**Kryteria akceptacji**:
-	Formularz: email, hasło
-	Opcjonalnie: przycisk "Zaloguj przez Google" (OAuth 2.0)
-	Weryfikacja credentials
-	Generowanie Access Token (JWT, 1h) i Refresh Token (7 dni)
-	Zapisanie w HttpOnly cookies (flags: HttpOnly, Secure, SameSite=Strict)
-	Przekierowanie na dashboard (Student/Nauczyciel/Admin)
-	Błąd: "Nieprawidłowy email lub hasło"

### US-03: Wylogowanie użytkownika
**Jako** zalogowany użytkownik\
**Chcę** wylogować się\
**Aby** zabezpieczyć konto

**Priorytet**: Must Have | **Punkty**: 2

**Kryteria akceptacji**:
-	Przycisk "Wyloguj" w menu
-	Request: POST /api/auth/logout
-	Usunięcie cookies (access_token, refresh_token)
-	Refresh token na blacklist (Redis)
-	Przekierowanie na login
-	401 Unauthorized przy próbie dostępu do chronionych zasobów

## Moduł 2: Zarządzanie kursami (Nauczyciel)
### US-04: Tworzenie kursu
**Jako** nauczyciel\
**Chcę** utworzyć kurs\
**Aby** udostępnić materiały studentom

**Priorytet**: Must Have | **Punkty**: 3

**Kryteria akceptacji**:
-	Formularz: nazwa, opis (Markdown), status (Draft/Published)
-	Walidacja: nazwa nie pusta
-	Zapis: InstructorId = userId, Status = Draft, CreatedAt = now
-	Przekierowanie do zarządzania rozdziałami
-	Komunikat: "Kurs utworzony. Dodaj rozdziały."

### US-05: Dodawanie rozdziału
**Jako** nauczyciel\
**Chcę** dodać rozdział\
**Aby** uporządkować materiały

**Priorytet**: Must Have | **Punkty**: 2

**Kryteria akceptacji**:
-	Formularz: tytuł rozdziału
-	Auto OrderIndex (ostatni + 1)
-	Zmiana kolejności (strzałki góra/dół lub drag&drop)
-	Rozdział pusty (0 podrozdziałów)
-	Endpoint: POST /api/courses/{courseId}/chapters

### US-06: Dodawanie podrozdziału
**Jako** nauczyciel\
**Chcę** dodać podrozdział\
**Aby** szczegółowo podzielić materiał

**Priorytet**: Must Have | **Punkty**: 2

**Kryteria akceptacji**:
-	Formularz: tytuł
-	Auto generowanie numeru (np. "1.1", "2.3")
-	Auto OrderIndex
-	Zmiana kolejności (↑↓)
-	Podrozdział pusty (0 bloków)
-	Endpoint: POST /api/chapters/{chapterId}/subchapters

### US-07: Dodawanie bloku teorii
**Jako** nauczyciel\
**Chcę** dodać teorię\
**Aby** przekazać wiedzę studentom

**Priorytet**: Must Have | **Punkty**: 5

**Kryteria akceptacji**:
-	Formularz: tytuł, treść (Markdown editor z toolbar)
-	Podgląd na żywo (rendered Markdown)
-	Support: nagłówki, listy, kod (syntax highlighting), linki, obrazy
-	Zapis: Type = "Theory", Content = Markdown, OrderIndex = auto
-	Zmiana kolejności bloków (strzałkami)
-	Sanityzacja HTML (DOMPurify)

### US-08: Dodawanie bloku wideo
**Jako** nauczyciel\
**Chcę** dodać wideo\
**Aby** udostępnić materiał wideo

**Priorytet**: Must Have | **Punkty**: 3

**Kryteria akceptacji**:
-	Formularz: tytuł, URL YouTube
-	Walidacja URL: regex (youtube\.com\/watch\?v=|youtu\.be\/)([a-zA-Z0-9_-]+)
-	Wyodrębnienie Video ID
-	Podgląd: embedded YouTube player
-	Zapis: Type = "Video", VideoUrl = URL, OrderIndex = auto
-	Błąd: "Podaj prawidłowy link YouTube"

### US-09: Dodawanie bloku quiz
**Jako** nauczyciel\
**Chcę** dodać quiz\
**Aby** sprawdzić wiedzę studentów

**Priorytet**: Must Have | **Punkty**: 8

**Kryteria akceptacji**:
-	Formularz: tytuł quizu
-	Dodawanie pytań (min. 1):
    -	Treść pytania (Markdown support)
    -	Typ: Single Choice / Multiple Choice / True-False
    -	Odpowiedzi (min. 2): tekst, checkbox "poprawna"
    -	Wyjaśnienie (opcjonalne)
-	Walidacja:
    -	Min. 1 pytanie
    -	Min. 2 odpowiedzi per pytanie
    -	Min. 1 poprawna odpowiedź
    -	Single Choice: dokładnie 1 poprawna
-	Zapis: Quiz -> QuizQuestion -> QuizAnswer
-	CourseBlock: Type = "Quiz", QuizId
-	Zmiana kolejności pytań

### US-10: Dodawanie bloku zadania
**Jako** nauczyciel\
**Chcę** dodać zadanie\
**Aby** student mógł ćwiczyć

**Priorytet**: Must Have | **Punkty**: 3

**Kryteria akceptacji**:
-	Dropdown z listą zadań (Problem)
-	Filtrowanie: kategoria, poziom trudności
-	Wyszukiwarka po nazwie
-	Wybór 1 zadania
-	Opcja: "Utwórz nowe zadanie" -> US-11
-	Zapis: Type = "Problem", ProblemId, OrderIndex = auto
-	To samo zadanie można użyć w wielu kursach

### US-11: Tworzenie zadania praktycznego
**Jako** nauczyciel\
**Chcę** utworzyć zadanie\
**Aby** dodać do puli zadań

**Priorytet**: Must Have | **Punkty**: 8

**Kryteria akceptacji**:
-	Formularz:
    -	Tytuł
    -	Opis (Markdown): treść, przykłady, ograniczenia
    -	Poziom: Easy/Medium/Hard
    -	Tagi (multi-select): Algorytmy, Struktury danych, OOP, etc.
-	Dodawanie testów (min. 1):
    -	Input (textarea)
    -	Expected Output (textarea)
    -	Widoczność: Publiczny/Ukryty
-	Starter code (opcjonalnie, per język):
    -	Wybór języka
    -	Edytor z szablonem funkcji
-	Walidacja: tytuł/opis nie puste, min. 1 test
-	Zapis: Problem, TestCase[], StarterCode[], Problem_Tags
-	AuthorId = nauczyciel
-	Endpoint: POST /api/problems

### US-12: Publikowanie kursu
**Jako** nauczyciel\
**Chcę** opublikować kurs\
**Aby** studenci mogli się zapisać

**Priorytet**: Must Have | **Punkty**: 3

**Kryteria akceptacji**:
-	Przycisk "Opublikuj kurs" (w Draft)
-	Walidacja:
    -	Min. 1 rozdział
    -	Każdy rozdział: min. 1 podrozdział
    -	Każdy podrozdział: min. 1 blok
    -	Jeśli fail -> lista braków
-	Status -> Published
-	Kurs widoczny dla studentów
-	Po publikacji: brak edycji (read-only)
-	Komunikat: "Kurs opublikowany"
-	Endpoint: PUT /api/courses/{courseId}/publish

### US-13: Usuwanie elementów kursu
**Jako** nauczyciel\
**Chcę** usunąć rozdział/podrozdział/blok\
**Aby** poprawić strukturę przed publikacją

**Priorytet**: Must Have | **Punkty**: 3

**Kryteria akceptacji**:
-	Przycisk "Usuń" 🗑️ (tylko Draft)
-	Modal potwierdzenia: "Czy na pewno? Nieodwracalne"
-	Hard delete (kaskadowo):
    -	Rozdział -> podrozdziały -> bloki
    -	Podrozdział -> bloki
    -	Blok (Quiz/Problem pozostają w puli)
-	Przeliczenie OrderIndex
-	Komunikat sukcesu
-	Endpoint: DELETE /api/chapters/{id}, DELETE /api/subchapters/{id}, DELETE /api/blocks/{id}

## Moduł 3: Przeglądanie kursów (Student)
### US-14: Przeglądanie listy kursów
**Jako** student\
**Chcę** zobaczyć kursy\
**Aby** wybrać kurs do nauki

**Priorytet**: Must Have | **Punkty**: 3

**Kryteria akceptacji**:
-	Lista kursów Published (publiczne)
-	Karta kursu: tytuł, opis (200 znaków), nauczyciel, liczba rozdziałów
-	Status: "Zapisz się" / "Kontynuuj" + postęp
-	Wyszukiwarka (live search)
-	Filtr: nauczyciel
-	Sortowanie: data, nazwa, popularność
-	Kliknięcie -> szczegóły (US-16)
-	Paginacja: 12/strona
-	Endpoint: GET /api/courses?status=Published

### US-15: Zapisanie na kurs
**Jako** student\
**Chcę** zapisać się\
**Aby** rozpocząć naukę

**Priorytet**: Must Have | **Punkty**: 3

**Kryteria akceptacji**:
-	Przycisk "Zapisz się"
-	Zapis: StudentCourseProgress (StudentId, CourseId, EnrolledAt = now)
-	Walidacja: unique (StudentId, CourseId)
-	Przekierowanie do spisu treści (US-16)
-	Komunikat: "Zapisano na kurs. Powodzenia!"
-	Przycisk -> "Kontynuuj naukę"
-	Endpoint: POST /api/courses/{courseId}/enroll

### US-16: Struktura kursu (spis treści)
**Jako** student\
**Chcę** zobaczyć strukturę\
**Aby** śledzić postęp

**Priorytet**: Must Have | **Punkty**: 5

**Kryteria akceptacji**:
-	Drzewo: Rozdziały -> Podrozdziały -> Bloki
-	Ikony statusu: ukończony, w trakcie, nierozpoczęty
-	Pasek postępu: X/Y bloków (Z%)
-	Info: "Jesteś w: Rozdział X -> X.Y -> Blok Z"
-	Przycisk "Kontynuuj naukę" -> CurrentBlockId (lub pierwszy blok)
-	Sidebar: minimapa rozdziałów
-	Breadcrumbs: Kursy > Nazwa > Spis treści
-	Kliknięcie na strzałkę -> powrót do bloku
-	Endpoint: GET /api/courses/{courseId}/structure

### US-17: Przerabianie teorii
**Jako** student\
**Chcę** przeczytać teorię\
**Aby** zdobyć wiedzę

**Priorytet**: Must Have | **Punkty**: 3

**Kryteria akceptacji**:
-	Wyświetlanie: tytuł, treść Markdown (rendered)
-	Syntax highlighting dla kodu
-	Przycisk "Następny blok ->" (sticky)
-	Kliknięcie -> auto complete:
    -	POST /api/blocks/{blockId}/complete
    -	StudentBlockProgress: IsCompleted = true, CompletedAt = now
    -	StudentCourseProgress: LastActivityAt, CurrentBlockId = next
    -	Response: nextBlockId or null
    -	Przekierowanie do next lub spis treści
-	Przyciski: "← Poprzedni", "Spis treści"
-	Breadcrumbs
-	Markdown: marked.js + DOMPurify

### US-18: Oglądanie video
**Jako** student\
**Chcę** obejrzeć video\
**Aby** wizualnie zrozumieć temat

**Priorytet**: Must Have | **Punkty**: 2

**Kryteria akceptacji**:
-	Embedded YouTube player (responsive iframe)
-	Kontrolki: play, pause, seek, volume, fullscreen
-	Przycisk "Następny blok" -> auto complete (jak US-17)
-	Iframe: https://www.youtube.com/embed/{videoId}

### US-19: Rozwiązywanie quizu
**Jako** student\
**Chcę** rozwiązać quiz\
**Aby** sprawdzić wiedzę

**Priorytet**: Must Have | **Punkty**: 5

**Kryteria akceptacji**:
-	Pytania po kolei (1/5, 2/5...)
-	Wyświetlanie: treść, typ, odpowiedzi (radio/checkbox)
-	Przycisk "Sprawdź odpowiedź"
-	Feedback: ✅/❌, wyjaśnienie, poprawna odpowiedź
-	Przycisk "Następne pytanie"
-	Podsumowanie: X/Y (Z%), lista ✅/❌
-	Zapis: StudentQuizAttempt (Score, Answers JSON), StudentBlockProgress
-	Tylko 1 próba (unique: StudentId, QuizId)
-	Ponowna próba -> "Quiz ukończony. Wynik: X%"
-	Przycisk "Następny blok"
-	Endpoint: POST /api/quizzes/{quizId}/submit


## Moduł 4: Rozwiązywanie zadań (Student)
### US-20: Widok zadania
**Jako** student\
**Chcę** rozwiązać zadanie\
**Aby** przećwiczyć

**Priorytet**: Must Have | **Punkty**: 8

**Kryteria akceptacji**:
-	Wyświetlanie: tytuł, opis (Markdown), poziom, tagi
-	Dropdown: wybór języka (Language.IsEnabled = true)
-	Edytor Monaco:
    -	Syntax highlighting
    -	Numerowanie linii
    -	Autocomplete
    -	Indentacja auto
    -	Skróty (Ctrl+S, Ctrl+Z)
-	Starter code (jeśli jest) -> auto fill po wyborze języka
-	Przycisk "Prześlij rozwiązanie"
-	Po zaliczeniu (100% testów) -> StudentBlockProgress: IsCompleted = true
-	Link "Historia rozwiązań" (US-24)
-	Endpoint: GET /api/problems/{problemId}

### US-21: Przesłanie rozwiązania
**Jako** student\
**Chcę** przesłać rozwiązanie\
**Aby** system sprawdził poprawność

**Priorytet**: Must Have | **Punkty**: 13

**Kryteria akceptacji**:

Frontend:
-	Walidacja: kod nie pusty, język wybrany
-	POST /api/submissions (problemId, languageId, code)
-	Response: 202 Accepted + submissionId
-	UI: "Sprawdzanie..." (spinner)
-	WebSocket/SignalR: /submissions/{submissionId}/status
-	Real-time updates: Pending -> Running -> Completed

Backend:
-	Zapis Submission: Status = Pending, CreatedAt = now
-	Dodanie job do kolejki (Redis/RabbitMQ): submissionId

Runner Service:
1.	Pobierz job (submissionId)
2.	Pobierz: Submission, Problem, TestCase[], Language
3.	Language Executor (strategy): PythonExecutor, JavaExecutor, etc.
4.	Tmp workspace: /tmp/submissions/{submissionId}/
5.	Zapis kodu: solution.py / Solution.java / Program.cs
6.	Generowanie wrapper dla testów (Input -> Output)
7.	Docker run:
-	Image: Language.DockerImage
-	Volume: workspace -> /app
-	Flags: --memory, --cpus, --network=none, --pids-limit, --read-only
-	Timeout: Language.TimeoutSeconds
-	Command: Language.RunCommand
8.	Per test case: run, collect stdout/stderr/exitcode/time/memory
9.	Porównanie: stdout.trim() == ExpectedOutput.trim()
10.	Zapis: SubmissionTestResult (Status, ActualOutput, ErrorMessage, time, memory)
11.	Score = (passed / total) * 100
12.	Update Submission: Status = Completed, Score, time, memory, CompletedAt
13.	Cleanup workspace
14.	SignalR notify: status + score

Przekierowanie do US-22

### US-22: Wyniki sprawdzenia
**Jako** student\
**Chcę** zobaczyć wyniki\
**Aby** wiedzieć czy poprawnie

**Priorytet**: Must Have | **Punkty**: 5

**Kryteria akceptacji**:
-	Status ogólny (badge):
    -	Accepted (wszystkie pass)
    -	Wrong Answer
    -	Runtime Error
    -	Time Limit Exceeded
    -	Memory Limit Exceeded
    -	Compilation Error
-	Wynik: X/Y testów (Z%)
-	Progress bar
-	Wyniki per test:
    -	Publiczne: Input, Expected, Your Output, Status, Time, Memory
    -	Ukryte: tylko Status, Time, Memory (bez Input/Output)
-	Stdout/Stderr (jeśli były)
-	Compilation Error (jeśli był): message, line number
-	Statystyki: łączny czas, max pamięć, język, data
-	Akcje:
    -	"Spróbuj ponownie" -> wraca do edytora (kod zachowany)
    -	"Następny blok" (jeśli Accepted)
    -	"Historia rozwiązań"

### US-23: Historia rozwiązań
**Jako** student\
**Chcę** zobaczyć historię\
**Aby** śledzić postęp

**Priorytet**: Should Have | **Punkty**: 3

**Kryteria akceptacji**:
-	Tabela wszystkich submissions dla zadania:
    -	(numer próby)
    -	Data/czas
    -	Język
    -	Status (Accepted/Wrong Answer...)
    -	Wynik (X/Y testów)
    -	Czas wykonania
-	Najnowsze na górze
-	Przyciski:
    -	"Zobacz kod" -> read-only edytor
    -	"Zobacz wyniki" -> US-22
-	Endpoint: GET /api/problems/{problemId}/submissions

## Moduł 5: Komentarze i dyskusje
### US-24: Dodawanie komentarza
**Jako** student lub nauczyciel\
**Chcę** dodać komentarz pod blokiem\
**Aby** zadać pytanie lub pomóc

**Priorytet**: Must Have | **Punkty**: 5

**Kryteria akceptacji**:
-	Sekcja komentarzy pod blokami: teoria, video, zadanie
-	Textarea + przycisk "Dodaj komentarz"
-	Wyświetlanie: autor (imię, rola), treść, data
-	Sortowanie: najnowsze na górze
-	Autor może usunąć swój komentarz
-	Nauczyciel może odpowiedzieć (nested, 1 poziom)
-	Zapis: Comment (BlockId, UserId, Content, ParentCommentId nullable)
-	Endpoint: POST /api/blocks/{blockId}/comments

## Moduł 6: Przeglądanie postępów (Nauczyciel)
### US-25: Lista studentów w kursie
**Jako** nauczyciel\
**Chcę** zobaczyć studentów\
**Aby** monitorować postępy

**Priorytet**: Must Have | **Punkty**: 3

**Kryteria akceptacji**:
-	Lista studentów (StudentCourseProgress)
-	Wyświetlanie: imię, email, pasek postępu (%), aktualny rozdział/podrozdział/blok, ostatnia aktywność
-	Sortowanie: postęp, aktywność, nazwisko
-	Wyszukiwarka: nazwisko/email
-	Przycisk "Zobacz szczegóły" -> US-26
-	Endpoint: GET /api/courses/{courseId}/students

### US-26: Szczegółowy postęp studenta
**Jako** nauczyciel\
**Chcę** zobaczyć szczegóły\
**Aby** zrozumieć problemy studenta

**Priorytet**: Must Have | **Punkty**: 5

**Kryteria akceptacji**:
-	Dane: imię, email
-	Pasek postępu ogólny
-	Drzewo kursu: ✅/⏳/🔒 per blok
-	Szczegóły per blok:
    -	Teoria: przeczytane, data
    -	Video: obejrzane, data
    -	Quiz: wynik (X/Y, %), lista pytań z odpowiedziami (✅/❌)
    -	Zadania: wynik per zadanie, link "Zobacz kod"
-	Statystyki:
    -	Ukończone rozdziały: X/Y
    -	Ukończone podrozdziały: X/Y
    -	Ukończone bloki: X/Y
    -	Średnia quizów: %
    -	Średnia zadań: X/100
    -	Czas aktywności: Xh Ym
-	Przycisk "Wyślij email" (mailto:)
-	Endpoint: GET /api/courses/{courseId}/students/{studentId}/progress

### US-27: Przeglądanie kodu studenta
**Jako** nauczyciel\
**Chcę** zobaczyć kod\
**Aby** zrozumieć podejście i pomóc

**Priorytet**: Must Have | **Punkty**: 3

**Kryteria akceptacji**:
-	Lista wszystkich submissions studenta dla zadania
-	Tabela: data, język, status, wynik
-	Przycisk "Zobacz kod" -> edytor Monaco read-only:
    -	Kod z syntax highlighting
    -	Wyniki testów obok (pass/fail)
    -	Stdout/stderr
    -	Compilation errors
-	Brak edycji (read-only)
-	Brak code review / komentarzy w kodzie
-	Endpoint: GET /api/students/{studentId}/problems/{problemId}/submissions

## Moduł 7: Panel administracyjny
### US-28: Zarządzanie użytkownikami
**Jako** admin\
**Chcę** zarządzać użytkownikami\
**Aby** kontrolować dostęp

**Priorytet**: Should Have | **Punkty**: 3

**Kryteria akceptacji**:
-	Lista: email, imię, nazwisko, rola, status, data rejestracji
-	Wyszukiwarka: email/nazwisko
-	Filtr: rola
-	Edycja: zmiana roli, dezaktywacja
-	Usuwanie: hard delete + modal potwierdzenia
-	Dodawanie: formularz rejestracji (Admin tworzy konto)
-	Endpoint: GET /api/admin/users, PUT, DELETE, POST

### US-29: Dodawanie języka programowania
**Jako** admin\
**Chcę** dodać język\
**Aby** studenci mogli w nim rozwiązywać

**Priorytet**: Must Have | **Punkty**: 5

**Kryteria akceptacji**:
-	Formularz:
    -	Nazwa (Python)
    -	Wersja (3.11)
    -	Docker Image (python:3.11-alpine)
    -	File Extension (.py)
    -	Run Command (python3 /app/solution.py)
    -	Compile Command (opcjonalnie, dla Java)
    -	Timeout (s, default 5)
    -	Memory Limit (MB, default 256)
    -	CPU Limit (cores, default 0.5)
    -	Status (Aktywny/Wyłączony)
-	Przycisk "Test połączenia z Dockerem":
    -	Run simple "Hello World"
    -	Wyświetl success/error
-	Walidacja:
    -	Unikalność (Nazwa, Wersja)
    -	Docker Image format (regex)
    -	Timeout/Memory/CPU > 0
-	Zapis: Language
-	Język dostępny od razu (jeśli Aktywny)
-	Endpoint: POST /api/admin/languages

### US-30: Edycja języka
**Jako** admin\
**Chcę** edytować język\
**Aby** poprawić konfigurację

**Priorytet**: Must Have | **Punkty**: 3

**Kryteria akceptacji**:
-	Lista języków + przycisk "Edytuj"
-	Formularz: te same pola co US-29
-	Walidacja jak US-29
-	Przycisk "Test połączenia"
-	Update Language
-	Zmiany od razu aktywne
-	Endpoint: PUT /api/admin/languages/{id}

### US-31: Wyłączanie języka
**Jako** admin\
**Chcę** wyłączyć język\
**Aby** zapobiec niestabilności

**Priorytet**: Must Have | **Punkty**: 2

**Kryteria akceptacji**:
-	Toggle "Aktywny/Wyłączony" w liście
-	Wyłączony:
    -	Nie pojawia się w dropdown dla studentów
    -	Nie można przesłać nowego submission
    -	Istniejące submissions widoczne (historia)
-	Włączony:
    -	Pojawia się w dropdown
    -	Można przesyłać
-	Endpoint: PATCH /api/admin/languages/{id}/toggle

### US-32: Usuwanie języka
**Jako** admin\
**Chcę** usunąć język\
**Aby** oczyścić nieużywane

**Priorytet**: Should Have | **Punkty**: 2

**Kryteria akceptacji**:
-	Przycisk "Usuń"
-	Walidacja:
    -	Jeśli istnieją submissions -> nie można usunąć (error)
    -	Brak submissions -> można
-	Modal: "Czy na pewno?"
-	Hard delete
-	Endpoint: DELETE /api/admin/languages/{id}

### US-33: Dashboard systemu
**Jako** admin\
**Chcę** zobaczyć statystyki\
**Aby** monitorować platformę

**Priorytet**: Should Have | **Punkty**: 5

**Kryteria akceptacji**:
-	Metryki:
    -	Liczba użytkowników (total, studenci, nauczyciele)
    -	Liczba kursów (total, published)
    -	Liczba zadań
    -	Submissions (24h, 7d, total)
    -	Średni czas wykonania (ms)
    -	Błędy (24h): compilation, runtime, timeout
    -	Status języków (aktywne/wyłączone)
    -	Top 5 kursów (liczba studentów)
-	Wykres: Submissions w czasie (30 dni)
-	Lista ostatnich błędów (logi)
-	Endpoint: GET /api/admin/dashboard

## Moduł 8: Certyfikaty
### US-34: Generowanie certyfikatu
**Jako** student\
**Chcę** otrzymać certyfikat\
**Aby** potwierdzić wiedzę

**Priorytet**: Should Have | **Punkty**: 5

**Kryteria akceptacji**:
-	System sprawdza po każdym bloku: czy 100% kursu?
-	Warunek: wszystkie bloki IsCompleted = true
-	Jeśli 100% -> auto generowanie certyfikatu:
    -	PDF szablon:
        -	Logo platformy
        -	"Certyfikat ukończenia kursu"
        -	Nazwa kursu
        -	Imię i nazwisko studenta
        -	Data ukończenia
        -	Kod weryfikacyjny (UUID)
    -	Zapis: Certificate (VerificationCode, CertificateUrl)
    -	PDF storage: Azure Blob / lokalny /certificates/
-	Komunikat: "Gratulacje! Ukończyłeś kurs! 🎉"
-	Przycisk "Pobierz certyfikat" (download PDF)

### US-35: Weryfikacja certyfikatu
**Jako** osoba zewnętrzna\
**Chcę** zweryfikować certyfikat\
**Aby** upewnić się że autentyczny

**Priorytet**: Should Have | **Punkty**: 3

**Kryteria akceptacji**:
-	Publiczna strona: /verify-certificate
-	Formularz: "Kod weryfikacyjny"
-	Wyszukiwanie: Certificate.VerificationCode
-	Jeśli znaleziono:
    -	Wyświetl: imię, kurs, data, "✅ Autentyczny"
    -	Link do PDF
-	Nie znaleziono: "❌ Nie znaleziono"
-	Endpoint: GET /api/certificates/verify?code={code}