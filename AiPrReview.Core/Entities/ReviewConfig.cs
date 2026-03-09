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
    [Table(nameof(ReviewConfig))]
    public class ReviewConfig
    {
        [Key]
        public int Id { get; set; }
        public Guid RepositoryId { get; set; }
        public ReviewStrategy ReviewStrategy { get; set; }
        public int? MaxFilesPerBatch { get; set; }
        public int? MaxConcurrentFileReviews { get; set; }
    }
}
