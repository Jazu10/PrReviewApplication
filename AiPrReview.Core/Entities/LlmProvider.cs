using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AiPrReview.Core.Entities
{
    [Table(nameof(LlmProvider))]
    public class LlmProvider
    {
        [Key]
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public int Priority { get; set; }
        public ICollection<LlmProviderSetting> Settings { get; set; } = new List<LlmProviderSetting>();
    }
}
