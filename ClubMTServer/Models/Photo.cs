using System.ComponentModel.DataAnnotations;

namespace ClubMTServer.Models
{
    public class PhotoSession
    {
        public int Id { get; set; }
        
        [Required]
        public string Title { get; set; } = string.Empty; // e.g., "제 4회 정기 MT"
        
        public int Order { get; set; } // For sorting sessions
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public List<Photo> Photos { get; set; } = new();
    }

    public class Photo
    {
        public int Id { get; set; }
        
        public int PhotoSessionId { get; set; }
        
        [Required]
        public string Url { get; set; } = string.Empty;

        public string? ThumbnailUrl { get; set; }
        
        public string? Description { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
