using AiPrReview.Core.Enums;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AiPrReview.Core.Entities
{
    [Table(nameof(ProviderConfig))]
    public class ProviderConfig
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public ProviderType Provider { get; set; }

        [Required]
        [MaxLength(500)]
        public string AccessToken { get; set; } = string.Empty;

        [Required]
        [MaxLength(500)]
        public string ApiUrl { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        [Required]
        public ReviewStrategy ReviewStrategy { get; set; }

        [Required]
        public PublishMode PublishMode { get; set; }

        public int? MaxFilesPerBatch { get; set; }

        public int? MaxConcurrentFileReviews { get; set; }
    }
}