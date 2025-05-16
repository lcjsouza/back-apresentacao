namespace OpenAIChatApi.Models
{
    public class ChatRequest
    {
        public ChatRequest(string message)
        {
            Message = message;
        }

        public string? Message { get; internal set; }
    }
}
