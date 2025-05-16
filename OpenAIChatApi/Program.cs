using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using System.Text.Json;
using System.Text;
using System.Net.Http.Headers;

var builder = WebApplication.CreateBuilder(args);

// 🔥 Configuração de porta via Kestrel (boa prática)
var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
builder.WebHost.UseUrls($"http://*:{port}");

// Configura��o de CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

var geminiApiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY"); //Chave da API Gemini
var openAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY"); // Chave da API OpenAI

if (string.IsNullOrEmpty(geminiApiKey))
{
    throw new InvalidOperationException("A chave da API Gemini n�o foi configurada. Verifique a vari�vel de ambiente 'GEMINI_API_KEY'.");
}
if (string.IsNullOrEmpty(openAiKey))
{
    throw new InvalidOperationException("A chave da API OpenAI n�o foi configurada. Verifique a vari�vel de ambiente 'OPENAI_API_KEY'.");
}

// Cliente HTTP
var clientGemini = new HttpClient();
var clientOpenIA = new HttpClient();
clientOpenIA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", openAiKey);

// Lista de personas
string[] personas = ["adesao", "icatuSign", "sicred", "equilibrio", "persona"];


// Criar um endpoint para cada persona
foreach (var persona in personas)
{
    app.MapPost($"/api/chat/{persona}", async (HttpRequest request) =>
    {
        try
        {
            // Validar o corpo da requisi��o
            var body = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(request.Body);
            if (body == null || !body.ContainsKey("message"))
            {
                return Results.BadRequest(new { error = "O corpo da requisi��o deve conter a chave 'message'." });
            }

            var userMessage = body["message"];
            var llm = body.ContainsKey("llm") ? body["llm"] : "";

            // Carregar o arquivo de persona
            var personaPath = Path.Combine("Personas", $"{persona}.txt");
            if (!File.Exists(personaPath))
            {
                return Results.NotFound(new { error = $"O arquivo de persona '{persona}.txt' n�o foi encontrado." });
            }

            var systemPrompt = await File.ReadAllTextAsync(personaPath, Encoding.UTF8);

            string content = string.Empty;
            switch (llm)
            {
                case "gemini":
                    content = await CallGeminiAsync(clientGemini, systemPrompt, userMessage, geminiApiKey);
                    Console.WriteLine($"Gemini: {content}");
                    return Results.Json(
                        new { response = content },
                        new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping },
                        contentType: "application/json; charset=utf-8"
                    );
                case "openai":
                    content = await CallOpenIAAsync(clientOpenIA, systemPrompt, userMessage, openAiKey);
                    Console.WriteLine($"OpenIA: {content}");
                    return Results.Json(
                        new { response = content },
                        new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping },
                        contentType: "application/json; charset=utf-8"
                    );
                case "audio":
                    // Par�metros opcionais para o �udio
                    var model = body.ContainsKey("model") ? body["model"] : "gpt-4o-mini-tts";
                    var voice = body.ContainsKey("voice") ? body["voice"] : "shimmer"; //nova
                    var instructions = body.ContainsKey("instructions") ? body["instructions"] : "Speak in a cheerful and positive tone.";
                    var responseFormat = body.ContainsKey("response_format") ? body["response_format"] : "wav";

                    // Primeiro, obtenha a resposta textual do modelo
                    var textResponse = await CallOpenIAAsync(clientOpenIA, systemPrompt, userMessage, openAiKey);

                    // Depois, gere o �udio a partir da resposta textual
                    var audioBytes = await CallOpenAIAudioAsync(clientOpenIA, textResponse, model, voice, instructions, responseFormat);

                    // Retorne ambos: texto e �udio (como base64)
                    return Results.Json(
                        new { response = textResponse, audio = Convert.ToBase64String(audioBytes) },
                        new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping },
                        contentType: "application/json; charset=utf-8"
                    );

                default:
                    return Results.BadRequest(new { error = "O valor de 'llm' n�o � v�lido." });
            }
        }
        catch (Exception ex)
        {
            // Tratamento de erros gerais
            return Results.Json(
                new { error = "Ocorreu um erro interno no servidor.", details = ex.Message },
                statusCode: 500
            );
        }
    });
}

// Habilitar CORS
app.UseCors("AllowAll");
app.Run();


//LLM GEMINI
async Task<string> CallGeminiAsync(HttpClient client, string systemPrompt, string userMessage, string geminiApiKey)
{
    var payload = new
    {
        contents = new[]
        {
            new
            {
                parts = new[]
                {
                    new { text = $"{systemPrompt}\n{userMessage}" }
                }
            }
        }
    };

    var endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent?key={geminiApiKey}";

    var response = await client.PostAsync(
        endpoint,
        new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
    );

    if (!response.IsSuccessStatusCode)
    {
        var errorContent = await response.Content.ReadAsStringAsync();
        throw new Exception($"Erro ao se comunicar com a API Gemini: {errorContent}");
    }

    var json = await response.Content.ReadAsStringAsync();

    using var doc = JsonDocument.Parse(json);
    var content = doc.RootElement
        .GetProperty("candidates")[0]
        .GetProperty("content")
        .GetProperty("parts")[0]
        .GetProperty("text")
        .GetString();

    return content ?? string.Empty;
}

//LLM OPENAI
async Task<string> CallOpenIAAsync(HttpClient client, string systemPrompt, string userMessage, string geminiApiKey)
{
    // Montar o payload para a API OpenAI
    var payload = new
    {
        model = "gpt-3.5-turbo",
        messages = new[]
        {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userMessage }
                }
    };

    // Enviar a requisi��o para a API OpenAI
    var response = await client.PostAsync(
        "https://api.openai.com/v1/chat/completions",
        new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
    );

    if (!response.IsSuccessStatusCode)
    {
        var errorContent = await response.Content.ReadAsStringAsync();
        throw new Exception($"Erro ao se comunicar com a API OpenAI: {errorContent}");
    }
   
    // Processar a resposta da API OpenAI
    var json = await response.Content.ReadAsStringAsync();

    using var doc = JsonDocument.Parse(json);
    var content = doc.RootElement
        .GetProperty("choices")[0]
        .GetProperty("message")
        .GetProperty("content")
        .GetString();

    return content ?? string.Empty;
}

async Task<byte[]> CallOpenAIAudioAsync(HttpClient client, string input, string model, string voice, string instructions, string responseFormat)
{
    var payload = new
    {
        model = model,
        input = input,
        voice = voice,
        instructions = instructions,
        response_format = responseFormat
    };

    var response = await client.PostAsync(
        "https://api.openai.com/v1/audio/speech",
        new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
    );

    if (!response.IsSuccessStatusCode)
    {
        var errorContent = await response.Content.ReadAsStringAsync();
        throw new Exception($"Erro ao se comunicar com a API OpenAI Audio: {errorContent}");
    }

    // Retorna o �udio como array de bytes
    return await response.Content.ReadAsByteArrayAsync();
}
