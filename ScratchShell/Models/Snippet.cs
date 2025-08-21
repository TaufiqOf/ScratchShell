namespace ScratchShell.Models
{
    public class Snippet
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Code { get; set; }

        public Snippet()
        {
            Id = Guid.NewGuid().ToString();
        }
    }
}