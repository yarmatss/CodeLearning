# CodeLearning

Platforma webowa do nauki programowania i automatycznej oceny rozwiązań w technologii .NET
Czym jest ten projekt?
Platforma edukacyjna typu LeetCode/HackerRank umożliwiająca:
•	Studentom: naukę programowania przez rozwiązywanie zadań w wielu językach (Python, Java, C#, JavaScript), przerabianie kursów z teorią, video i quizami
•	Nauczycielom: tworzenie kursów (struktura: Kurs → Rozdziały → Podrozdziały → Bloki), zarządzanie zadaniami, śledzenie postępów studentów
•	Adminowi: zarządzanie systemem, konfiguracją języków programowania (Docker), użytkownikami
Kluczowe funkcjonalności
Dla Studenta:
•	Przeglądanie i zapisywanie się na kursy (publiczne)
•	Przerabianie materiałów:
•	Teoria (Markdown)
•	Video (YouTube embed)
•	Quiz (pytania single/multiple choice, tylko 1 próba)
•	Zadania praktyczne (edytor Monaco, wielojęzykowe)
•	Przesyłanie rozwiązań → automatyczna ocena w izolowanym kontenerze Docker
•	Historia rozwiązań, komentarze pod blokami
•	Certyfikat PDF po ukończeniu kursu (z kodem weryfikacyjnym)
Dla Nauczyciela:
•	Tworzenie kursów (Draft → Published, bez edycji po publikacji)
•	Struktura: Kurs → Rozdział → Podrozdział → Bloki (dowolna kolejność i liczba)
•	Typy bloków: Teoria, Video, Quiz, Zadanie (1 zadanie na blok)
•	Tworzenie zadań z testami (Input/Expected Output)
•	Przeglądanie postępów studentów (szczegółowe statystyki, kod rozwiązań read-only)
•	Brak code review - tylko podgląd
Dla Admina:
•	Zarządzanie użytkownikami (role: Student/Teacher/Admin)
•	Pełna konfiguracja języków w DB:
•	Nazwa, wersja, Docker image, komendy run/compile
•	Limity: timeout, memory, CPU
•	Włączanie/wyłączanie języków
•	Dashboard systemu (statystyki, monitoring)
Stack technologiczny
Backend:
•	.NET 10 (ASP.NET Core Web API)
•	ASP.NET Core Identity (zarządzanie użytkownikami i rolami)
•	Entity Framework Core 10 + PostgreSQL 18
•	JWT (HttpOnly Cookies) + OAuth 2.0 (Google)
•	Redis 8.4/RabbitMQ 4.2.1 (kolejka zadań)
•	Docker (wykonanie kodu w izolacji)
Frontend:
•	Angular 21
•	Monaco Editor (edytor kodu jak VS Code)
•	Angular Material 21
Runner Service:
•	Docker SDK (Docker.DotNet)
•	Multi-language support (strategy pattern: PythonExecutor, JavaExecutor, etc.)
•	Sandboxing: --network=none, --memory, --cpus, --read-only
Kluczowe decyzje architektoniczne
HttpOnly Cookies zamiast localStorage (bezpieczeństwo XSS/CSRF)
1 blok = 1 zadanie (uproszczenie struktury)
Języki w DB - pełna konfiguracja przez Admina (Docker image, limity)
Hard delete (bez soft delete)
Bloki bez blokad - wszystkie odblokowane, student idzie sekwencyjnie
Auto complete - przycisk "Następny blok" zapisuje jako przeczytane (bez checkboxów)
Quiz tylko 1 próba, zadania bez ograniczeń
Input/Output testy (nie kod testów jednostkowych)
Dokumentacja
•	35 User Stories (Must Have: 28, Should Have: 7)
•	21 encji w bazie danych (ERD w PlantUML)
•	Moduły: Autentykacja, Kursy, Zadania, Komentarze, Panel Admin, Certyfikaty

Bezpieczeństwo
Autentykacja i Autoryzacja:
•	ASP.NET Core Identity (zarządzanie użytkownikami, ról, haseł)
•	JWT w HttpOnly Cookies (ochrona przed XSS)
•	Refresh Token w Redis z blacklistą
•	OAuth 2.0 (Google login) przez Identity providers
•	Policy-based authorization ([Authorize(Policy = "TeacherOnly")])
•	GUID dla Id użytkowników (zamiast int)
•	PBKDF2 password hashing z salt (built-in Identity)
•	Account lockout po 5 nieudanych próbach logowania
Izolacja i Walidacja:
•	Docker sandboxing (network isolation, resource limits)
•	Input sanitization (Markdown → HTML: DOMPurify)
•	Rate limiting, CORS, HTTPS
•	Antiforgery tokens dla operacji mutujących