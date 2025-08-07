using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Art_Touch.ViewModels
{
   
        public class CreateProductViewModel
        {
            public string Name { get; set; } = string.Empty;

            public string Description { get; set; } = string.Empty;

            [Required]
            public decimal OriginalPrice { get; set; }

            public decimal? DiscountPrice { get; set; }

            public bool IsBestseller { get; set; }

            public bool IsNewArrival { get; set; }

            public bool IsActive { get; set; } = true;

            [Required]
            public int CategoryId { get; set; }

            [Required]
            public IFormFile CoverImage { get; set; }

            public List<IFormFile> AdditionalImages { get; set; } = new();

            // Sizes
            public List<ProductSizeViewModel> Sizes { get; set; } = new();
        }

        public class ProductSizeViewModel
        {
            public string Size { get; set; } = string.Empty;

            public int QuantityInStock { get; set; }
        }
    
}
