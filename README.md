# Zadanie rekrutacyjne
Program umożliwia:
- Tworzenie pustej bazy danych z możliwościa zaimportowania metadanych
- Exportowanie metadanych: domen, tabel (z kolumnami) oraz procedur
- Aktualizowanie istniejącej bazy danych

Nazwa metadanych, które się zapisuje bądź wczytuje jest zhardocodowana (databaseMeta.json), oraz program uwzględnia tylko tą nazwe metadanych. Więc próbując wczytywać bądź zapisywać używamy tylko ścieżki, bez pliku końcowego.

Zadanie testowałem otwierając wygenerowany program .exe przez cmd, z wymaganymi argumentami.

Wartości "Connection", które u mnie działały generowałem za pomocą:
~~~
var connectionString = new FbConnectionStringBuilder
{
    Database = @"C:\db\fb5\DATABASE.FDB",
    ServerType = FbServerType.Default,  // 0
    UserID = "SYSDBA",
    Password = "masterkey",
    DataSource = "localhost",           // nazwa hosta / IP serwera
    Port = 3050,                        // domyślny port Firebird
    ClientLibrary = "fbclient.dll"
}.ToString();
~~~
