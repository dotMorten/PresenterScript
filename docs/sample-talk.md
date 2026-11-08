# Building a Weather CLI in 15 Minutes

## Setup
- [ ] Set display to 1080p and font size to 150%
- [ ] Close Teams and Outlook
- [ ] Open an empty folder in the terminal

---
@0:00
## Welcome
Introduce yourself. Today we build a tiny weather tool with **.NET** and a free web API.

---
@2:00
### Create the project
Start in an empty folder.

```
dotnet new console -n WeatherCli
```

Step into the new folder.

```
cd WeatherCli
```

---
@5:00
### Call the API
Replace `Program.cs`. Point out the `HttpClient` and the *top-level statements*.

```csharp
using System.Net.Http.Json;

var http = new HttpClient();
var url = "https://api.open-meteo.com/v1/forecast?latitude=47.6&longitude=-122.3&current=temperature_2m";
var data = await http.GetFromJsonAsync<Forecast>(url);
Console.WriteLine($"It's {data!.Current.Temperature_2m} °C in Seattle");

record Forecast(Current Current);
record Current(double Temperature_2m);
```

### Run it
```
dotnet run
```
Wait for the laugh about the Seattle weather. ☔

---
@10:00
### Make it pretty
Add a little color to the output.

```csharp
Console.ForegroundColor = ConsoleColor.Cyan;
```

---
@13:00
## Wrap up
- Recap: project, HTTP call, records
- Share the repo link
- Questions?

@15:00
