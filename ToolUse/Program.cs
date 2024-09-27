using System.Reflection;
using System.Text;
using System.Text.Json;
using OpenAI;
using OpenAI.Chat;
using PDFiumCore;
using static PDFiumCore.fpdf_text;
using static PDFiumCore.fpdfview;
// ReSharper disable InconsistentNaming

namespace ToolUse;


/// <summary>
/// The idea of this application is to test different small (large language models)
/// in the different characteristics available in foundational models
/// - [X] Basic Spanish Language Completion
/// - [X] Spanish Summarization
/// - [X] Few-shot prompting
/// - [X] Tool Use (couldnt make it work with tool calling)
/// - [X] Json Mode Output
/// - [X] Parsing Multiple files
/// </summary>
internal class Program
{
    private const bool UseTogetherAi = false;
    static async Task Main(string[] args)
    {
        FPDF_InitLibrary();

        //ollama
        var aiService = new AiService("http://localhost:11434/v1", "llama3.2", "ollama");
        //gpt-4o-mini
        //var aiService = new AiService("https://api.openai.com/v1", "gpt-4o-mini", Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty);
        //TogetherAI
        //var aiService = new AiService("https://api.together.xyz/v1", "meta-llama/Meta-Llama-3.1-8B-Instruct-Turbo", Environment.GetEnvironmentVariable("TOGETHERAI_API_KEY") ?? string.Empty);
        //LM Studio
        //var aiService = new AiService("http://localhost:11434/v1", "lmstudio-community/Llama-3.2-3B-Instruct-GGUF", "lm-studio");

        await BasicPromptTesting(aiService);
        //await ProcessFolder(aiService, @"<Folder with a bunch of PDF FILEs>");

        FPDF_DestroyLibrary();
    }

 

    static async Task BasicPromptTesting(AiService aiService)
    {
        //Generar un resumen de un contenido
        string textStory01 = await File.ReadAllTextAsync("Historia01.txt");
        string textStory02 = await File.ReadAllTextAsync("Historia02.txt");
        string textStory03 = await File.ReadAllTextAsync("Historia03.txt");


        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("Spanish Summarization:");
        var response = await aiService.CompleteAsync($"Generame de 1 parrafo de la siguiente historia:\n{textStory01}", "Eres un bot que resume historias, responde solo en español");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(response);

        //single-shot example

        ChatMessage[] singleShotMessages =
        [
            new SystemChatMessage("Eres un bot extractor de nombres de historias"),
            new UserChatMessage($"Con la siguiente historia\n{textStory03}, Dame una lista de todos los nombres")
        ];
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"Single-Shot Prompting:");
        var singleShotNames = await aiService.CompleteAsync(singleShotMessages);
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(singleShotNames);


        //few-shot example
        //the results with few-shotting looks pretty well for a model this tiny. the format from the previous prompts are mantainted
        //the only real issue is that in some cases the names are not found compared to the zero-shot example, but it still lists 3 entries.
        //but the format is kept 100% of the tries
        ChatMessage[] fewShotMessages =
        [
            new SystemChatMessage("Eres un bot extractor de nombres de historias"),
            new UserChatMessage($"Con la siguiente historia:\n{textStory01}, Dame una lista de todos los nombres"),
            new AssistantChatMessage("La Última Partida:\n\tMartín (28 años)\n\tClara (26 años)\n\tHugo (27 años)\n"),
            new UserChatMessage($"Con la siguiente historia:\n{textStory02}, Dame una lista de todos los nombres"),
            new AssistantChatMessage("El Último Viaje:\n\tPedro (30 años)\n\tSofía (29 años)\n\tMiguel (31 años)\n"),
            new UserChatMessage($"Con la siguiente historia\n{textStory03}, Dame una lista de todos los nombres")
        ];

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"Few-Shot Prompting:");
        var responseNames = await aiService.CompleteAsync(fewShotMessages);
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(responseNames);


        //tool call example
        string jsonSchema = """
                                {
                                  "type": "object",
                                  "properties": {
                                    "titulo": {
                                      "type": "string",
                                      "description": "El nombre de la historia."
                                    },
                                    "personas": {
                                      "type": "array",
                                      "description": "Lista de personajes con su nombre y edad.",
                                      "items": {
                                        "type": "object",
                                        "properties": {
                                          "nombre": {
                                            "type": "string",
                                            "description": "El nombre del personaje."
                                          },
                                          "edad": {
                                            "type": "integer",
                                            "description": "La edad del personaje."
                                          }
                                        },
                                        "required": ["nombre", "edad"]
                                      }
                                    },
                                    "resumen": {
                                      "type": "string",
                                      "description": "Un resumen de la historia."
                                    }
                                  },
                                  "required": ["titulo", "personas", "resumen"]
                                }
                                """;

        var tool = aiService.GetTool("FuncHistoria", "Elementos de la historia", jsonSchema);
        ChatCompletionOptions options = new()
        {
            Tools = { tool },
            //looks like llama doesnt like these options 
            //ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat(),
            //ToolChoice = ChatToolChoice.CreateRequiredChoice()
        };
        Console.ForegroundColor = ConsoleColor.Yellow;

        //Notes: for some reason tool calling doesnt work while serving from lm-studio. 
        //but it works 100% of the time when serving from ollama.
        //also together AI doesnt support function calling as of 2024-09-26 
        //so the only real way to test this is using ollama
        Console.WriteLine($"Tool Call:");
        var toolResponse = await aiService.CompleteAsync([
            new SystemChatMessage("Reply with valid json, dont include any additional text or markdown. dont include initial or ending backquotes"),
            new UserChatMessage(@$"
                Con la siguiente historia en tu contexto:
                {textStory03}
                Extrae los siguientes datos de la historia:
                1.- El titulo de la historia. (titulo)
                2.- Lista de personajes con su nombre y edad. (personas)
                3.- Un resumen de la historia.(resumen)

                Responde solo el json valido y parseable con la estructura y los datos que se requieren.
            ")
        ], options);
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(toolResponse);



        //simple COT


        //COT it looks like llama3.2 was trained using CoT in the training data
        //its very good at explaining itself just by asking to do it
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("CoT Simple Example:");
        var cotResponse = await aiService.CompleteAsync("¿\"somos\" es un palindromo? explicame tu razonamiento", "Eres un bot con razonamiento avanzado, eres bueno para describir lo que piensas y solo respondes en español");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(cotResponse);
        
        //a more detailed prompt with COT
        //when i try to make it follow a different chain of tought it provides false answers.
        //having it follow a new step of reasoning makes it worse at answering
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("CoT Complex Example:");
        var cotComplexResponse = await aiService.CompleteAsync(
            """
                "somos" es un palindromo? explicame tu razonamiento.
                describe los siguientes pasos para saber si "somos" es un palindromo:
                Paso 1.- ¿Cuales son las reglas de un palindromo?.
                Paso 2.- Aplica las reglas de un palindromo a la palabra "somos".
                Paso 3.- Provee la respuesta.
                """,
            "Eres un bot con razonamiento avanzado, eres bueno para describir lo que piensas y solo respondes en español");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(cotComplexResponse);
    }


    /// <summary>
    /// Parses files with CATALOGO DE CONCEPTOS FROM https://compranet.sinaloa.gob.mx/secretaria-de-obras-publicas
    /// 
    /// </summary>
    /// <param name="aiService"></param>
    /// <param name="folder"></param>
    /// <returns></returns>
    static async Task ProcessFolder(AiService aiService, string folder)
    {
        foreach (var filename in Directory.EnumerateFiles(folder, "*.pdf", SearchOption.AllDirectories))
        {
            if (!filename.Contains("CONCEPTOS", StringComparison.OrdinalIgnoreCase)) continue;
            var documenT = FPDF_LoadDocument(filename, null);
            int pageCount = FPDF_GetPageCount(documenT);
            for (int i = 0; i < pageCount; i++)
            {
                var pageT = FPDF_LoadPage(documenT, i);
                var pageTextT = FPDFTextLoadPage(pageT);
                string pageText = GetString(pageTextT);




                if (pageText.Length > 200)
                {
                    var tool = aiService.GetTool("ExtractorConceptos", "Extrae Catalogo de Conceptos",
                        """
                        {
                          "type": "object",
                          "properties": {
                            "conceptos": {
                              "type": "array",
                              "description": "Una lista de conceptos del documento de catalogo de conceptos",
                              "items": {
                                "type": "object",
                                "properties": {
                                  "clave": {
                                    "type": "string",
                                    "description": "clave del concepto"
                                  },
                                  "descripcion": {
                                    "type": "string",
                                    "description": "descripcion del concepto"
                                  }
                                },
                                "required": ["clave", "description"]
                              }
                            }
                          },
                          "required": ["conceptos"]
                        }
                        """);

                    ChatMessage[] chatMessages = [
                        new SystemChatMessage("Eres un bot de extraccion de datos de catalogos de conceptos, responde solo con json valido."),
                        new UserChatMessage($@"Con los siguientes datos de un catalogo de conceptos \n```\n{pageText}\n```\n 
                            Extrae una lista de conceptos con un arreglo de clave y descripcion con el formato de json: {{conceptos: [{{clave: ""Clave del concepto"", descripcion: ""Descripcion del concepto""}}]}}.")
                    ];
                    var options = new ChatCompletionOptions()
                    {
                        Tools = { tool }
                    };
                    var response = await aiService.CompleteAsync(chatMessages, options);
                    try
                    {
                        var parsed = JsonSerializer.Deserialize<Concepto>(response);
                        Console.WriteLine($"Parsed Correctly");
                    }
                    catch
                    {
                        Console.WriteLine($"Failed to parse Json");
                    }

                    //Console.WriteLine(response);

                }

                FPDFTextClosePage(pageTextT);
                FPDF_ClosePage(pageT);
            }
            FPDF_CloseDocument(documenT);

        }
    }

    static string GetString(FpdfTextpageT pageTextT)
    {
        unsafe
        {
            int characterCount = FPDFTextCountChars(pageTextT);
            Span<byte> txt = new byte[characterCount * 2 + 1];
            fixed (byte* txtPtr = txt)
            {
                FPDFTextGetText(pageTextT, 0, characterCount, ref *(ushort*)txtPtr);
            }

            string pageText = Encoding.Unicode.GetString(txt);

            return pageText;


        }
    }
}


class Concepto
{
    public required List<ClaveDescripcion> conceptos { get; set; }
}


class ClaveDescripcion
{
    public required string clave { get; set; }
    public required string descripcion { get; set; }
}