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
    [Table(nameof(RepositoryConfig))]
    public class RepositoryConfig
    {
        [Key]
        public Guid Id { get; set; }
        public string ProjectName { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public ProviderType Provider { get; set; }
        public string ApiUrl { get; set; } = string.Empty;
        public string AccessToken { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }
}
