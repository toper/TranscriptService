namespace TranscriptService.Models
{
    public class Result<T>
    {
        public Result()
        {
            Errors = new List<string>();
        }

        public List<string> Errors { get; set; }

        public bool IsValid => Errors.Count == 0;

        public T Data { get; set; }

        public string ToString(string separator)
        {
            return string.Join(separator, Errors);
        }
    }
}
