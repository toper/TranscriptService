namespace TranscriptService.API.Utils
{
    public static class SystemTime
    {
        public static Func<DateTime> Now = () => DateTime.Now;
        public static Func<DateTime> UtcNow = () => DateTime.UtcNow;
    }
}
