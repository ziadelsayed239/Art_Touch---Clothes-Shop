using Microsoft.AspNetCore.Mvc;
using DAL.Data.Models;
using DAL.Data;
using Microsoft.EntityFrameworkCore;
using Art_Touch.ViewModels;
using Microsoft.AspNetCore.Hosting;

namespace Art_Touch.Controllers
{
    public class AdminController : Controller
    {
        private readonly StoreDbContext _context;
        private readonly ILogger<AdminController> _logger;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public AdminController(StoreDbContext context, ILogger<AdminController> logger, IWebHostEnvironment webHostEnvironment)
        {
            _context = context;
            _logger = logger;
            _webHostEnvironment = webHostEnvironment;
        }

        public async Task<IActionResult> Dashboard()
        {
            var totalProducts = await _context.Products.CountAsync();
            var totalOrders = await _context.Orders.CountAsync();
            var totalCategories = await _context.Categories.CountAsync();
            var totalRevenue = await _context.Orders
                .Where(o => o.Status == OrderStatus.Completed)
                .SumAsync(o => o.TotalAmount);

            var heroImage = await _context.HeroImages.FirstOrDefaultAsync();
            ViewBag.HeroImageUrl = heroImage?.ImageURL;

            ViewBag.TotalProducts = totalProducts;
            ViewBag.TotalOrders = totalOrders;
            ViewBag.TotalCategories = totalCategories;
            ViewBag.TotalRevenue = totalRevenue;
            
            return View();
        }


        public async Task<IActionResult> Products()
        {
            var products = await _context.Products
                .Include(p => p.Category)
                .Include(p => p.Sizes)
                .ToListAsync();
            return View(products);
        }

        public async Task<IActionResult> CreateProduct()
        {
            ViewBag.Categories = await _context.Categories.ToListAsync();
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> CreateProduct(Product product, IFormFile coverImage, List<IFormFile> additionalImages, List<ProductSize> Sizes)
        {
            _logger.LogInformation("CreateProduct called with product: {ProductName}", product.Name);
            
            try
            {
                if (ModelState.IsValid)
                {
                    _logger.LogInformation("ModelState is valid");
                    
                    // Set default values
                    product.IsActive = product.IsActive;
                    product.IsNewArrival = product.IsNewArrival;
                    product.IsBestseller = product.IsBestseller;
                    
                    // Initialize collections if null
                    if (product.Images == null)
                        product.Images = new List<ProductImage>();
                    if (product.Sizes == null)
                        product.Sizes = new List<ProductSize>();
                    
                    _context.Products.Add(product);
                    await _context.SaveChangesAsync();
                    
                    _logger.LogInformation("Product saved with ID: {ProductId}", product.Id);
                    
                    // Handle cover image
                    if (coverImage != null && coverImage.Length > 0)
                    {
                        _logger.LogInformation("Processing cover image");
                        await HandleCoverImageUpload(product.Id, coverImage);
                    }
                    
                    // Handle additional images
                    if (additionalImages != null && additionalImages.Count > 0)
                    {
                        _logger.LogInformation("Processing {ImageCount} additional images", additionalImages.Count);
                        await HandleAdditionalImagesUpload(product.Id, additionalImages);
                    }
                    
                    // Handle sizes
                    if (Sizes != null && Sizes.Any())
                    {
                        _logger.LogInformation("Processing {SizeCount} sizes", Sizes.Count);
                        foreach (var size in Sizes)
                        {
                            if (!string.IsNullOrEmpty(size.Size) && size.QuantityInStock >= 0)
                            {
                                size.ProductId = product.Id;
                                _context.ProductSizes.Add(size);
                                _logger.LogInformation("Added size: {Size} with quantity: {Quantity}", size.Size, size.QuantityInStock);
                            }
                        }
                        await _context.SaveChangesAsync();
                        _logger.LogInformation("Sizes saved successfully");
                    }
                    
                    TempData["SuccessMessage"] = "Product created successfully!";
                    return RedirectToAction("Products");
                }
                else
                {
                    _logger.LogWarning("ModelState is invalid. Errors: {Errors}", 
                        string.Join(", ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating product: {Message}", ex.Message);
                TempData["ErrorMessage"] = "Error creating product. Please try again.";
            }
            
            ViewBag.Categories = await _context.Categories.ToListAsync();
            return View(product);
        }

        public async Task<IActionResult> EditProduct(int id)
        {
            var product = await _context.Products
                .Include(p => p.Sizes)
                .FirstOrDefaultAsync(p => p.Id == id);
            if (product == null) return NotFound();
            
            ViewBag.Categories = await _context.Categories.ToListAsync();
            return View(product);
        }

        [HttpPost]
        public async Task<IActionResult> EditProduct(Product product, IFormFile coverImage, List<IFormFile> additionalImages, List<ProductSize> Sizes)
        {
            try
            {
                if (ModelState.IsValid)
                {
                    var existingProduct = await _context.Products
                        .Include(p => p.Sizes)
                        .FirstOrDefaultAsync(p => p.Id == product.Id);
                    
                    if (existingProduct == null) return NotFound();
                    
                    // Update product properties
                    existingProduct.Name = product.Name;
                    existingProduct.Description = product.Description;
                    existingProduct.OriginalPrice = product.OriginalPrice;
                    existingProduct.DiscountPrice = product.DiscountPrice;
                    existingProduct.CategoryId = product.CategoryId;
                    existingProduct.IsActive = product.IsActive;
                    existingProduct.IsNewArrival = product.IsNewArrival;
                    existingProduct.IsBestseller = product.IsBestseller;
                    
                    // Update sizes
                    if (Sizes != null && Sizes.Any())
                    {
                        // Remove existing sizes
                        _context.ProductSizes.RemoveRange(existingProduct.Sizes);
                        
                        // Add new sizes
                        foreach (var size in Sizes)
                        {
                            if (!string.IsNullOrEmpty(size.Size) && size.QuantityInStock >= 0)
                            {
                                size.ProductId = product.Id;
                                _context.ProductSizes.Add(size);
                            }
                        }
                    }
                    
                    _context.Products.Update(existingProduct);
                    await _context.SaveChangesAsync();
                    
                    // Handle new cover image
                    if (coverImage != null && coverImage.Length > 0)
                    {
                        // Remove existing cover image
                        var existingCover = await _context.ProductImages
                            .Where(pi => pi.ProductId == product.Id && pi.IsCover)
                            .FirstOrDefaultAsync();
                        if (existingCover != null)
                        {
                            _context.ProductImages.Remove(existingCover);
                        }
                        
                        await HandleCoverImageUpload(product.Id, coverImage);
                    }
                    
                    // Handle new additional images
                    if (additionalImages != null && additionalImages.Count > 0)
                    {
                        await HandleAdditionalImagesUpload(product.Id, additionalImages);
                    }
                    
                    TempData["SuccessMessage"] = "Product updated successfully!";
                    return RedirectToAction("Products");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating product: {Message}", ex.Message);
                TempData["ErrorMessage"] = "Error updating product. Please try again.";
            }
            
            ViewBag.Categories = await _context.Categories.ToListAsync();
            return View(product);
        }

        [HttpPost]
        public async Task<IActionResult> DeleteProduct(int id)
        {
            try
            {
                var product = await _context.Products
                    .Include(p => p.Sizes)
                    .Include(p => p.Images)
                    .FirstOrDefaultAsync(p => p.Id == id);
                
                if (product != null)
                {
                    // Remove associated sizes and images
                    if (product.Sizes != null)
                        _context.ProductSizes.RemoveRange(product.Sizes);
                    if (product.Images != null)
                        _context.ProductImages.RemoveRange(product.Images);
                    
                    _context.Products.Remove(product);
                    await _context.SaveChangesAsync();
                    
                    TempData["SuccessMessage"] = "Product deleted successfully!";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting product: {Message}", ex.Message);
                TempData["ErrorMessage"] = "Error deleting product. Please try again.";
            }
            return RedirectToAction("Products");
        }

        // Categories Management
        public async Task<IActionResult> Categories()
        {
            var categories = await _context.Categories
                .Include(c => c.Products)
                .ToListAsync();
            return View(categories);
        }

        public IActionResult CreateCategory()
        {
            return View(new CategoryViewModel());
        }


        [HttpPost]
        public async Task<IActionResult> CreateCategory(CategoryViewModel model)
        {
            if (ModelState.IsValid)
            {
                var category = new Category
                {
                    Name = model.Name,
                    Description = model.Description,
                    IsActive = model.IsActive
                };

                if (model.ImageFile != null)
                {
                    var fileName = Guid.NewGuid() + Path.GetExtension(model.ImageFile.FileName);
                    var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/images/categories", fileName);

                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await model.ImageFile.CopyToAsync(stream);
                    }

                    category.ImageUrl = "/images/categories/" + fileName;
                }

                _context.Categories.Add(category);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Category created successfully!";
                return RedirectToAction("Categories");
            }

            return View(model);
        }

        public async Task<IActionResult> EditCategory(int id)
        {
            var category = await _context.Categories.FindAsync(id);
            if (category == null)
                return NotFound();

            var model = new CategoryViewModel
            {
                Id = category.Id,
                Name = category.Name,
                Description = category.Description,
                IsActive = category.IsActive,
                ExistingImageUrl = category.ImageUrl
            };

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> EditCategory(CategoryViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var category = await _context.Categories.FindAsync(model.Id);
            if (category == null)
                return NotFound();

            category.Name = model.Name;
            category.Description = model.Description;

            // ·Ê Ã«·ﬂ ’Ê—… ÃœÌœ…
            if (model.ImageFile != null)
            {
                // «Õ–› «·ﬁœÌ„… ·Ê „ÊÃÊœ…
                if (!string.IsNullOrEmpty(category.ImageUrl))
                {
                    var oldPath = Path.Combine(_webHostEnvironment.WebRootPath, category.ImageUrl.TrimStart('/'));
                    if (System.IO.File.Exists(oldPath))
                        System.IO.File.Delete(oldPath);
                }

                // «Õ›Ÿ «·ÃœÌœ…
                string fileName = Guid.NewGuid() + Path.GetExtension(model.ImageFile.FileName);
                string filePath = Path.Combine(_webHostEnvironment.WebRootPath, "images/categories", fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await model.ImageFile.CopyToAsync(stream);
                }

                category.ImageUrl = "/images/categories/" + fileName;
            }

            _context.Categories.Update(category);
            await _context.SaveChangesAsync();

            return RedirectToAction("Categories");
        }



        [HttpPost]
        public async Task<IActionResult> AddCategory([FromBody] Category category)
        {
            if (ModelState.IsValid)
            {
                category.IsActive = true;
                _context.Categories.Add(category);
                await _context.SaveChangesAsync();
                return Json(new { success = true });
            }
            return Json(new { success = false, message = "Invalid category data" });
        }

        [HttpPost]
        public async Task<IActionResult> UpdateCategory([FromBody] Category category)
        {
            if (ModelState.IsValid)
            {
                _context.Categories.Update(category);
                await _context.SaveChangesAsync();
                return Json(new { success = true });
            }
            return Json(new { success = false, message = "Invalid category data" });
        }

        [HttpPost]
        public async Task<IActionResult> ToggleCategoryStatus(int categoryId, bool isActive)
        {
            var category = await _context.Categories.FindAsync(categoryId);
            if (category != null)
            {
                category.IsActive = isActive;
                _context.Categories.Update(category);
                await _context.SaveChangesAsync();
                return Json(new { success = true });
            }
            return Json(new { success = false, message = "Category not found" });
        }

        [HttpPost]
        public async Task<IActionResult> DeleteCategory(int categoryId)
        {
            var category = await _context.Categories.FindAsync(categoryId);
            if (category == null)
            {
                return Json(new { success = false, message = "Category not found" });
            }

            var hasProducts = await _context.Products.AnyAsync(p => p.CategoryId == categoryId);
            if (hasProducts)
            {
                return Json(new { success = false, message = "Cannot delete category that has products" });
            }

            // Õ–› «·’Ê—… „‰ «·”Ì—›—
            if (!string.IsNullOrEmpty(category.ImageUrl))
            {
                var path = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", category.ImageUrl.TrimStart('/'));
                if (System.IO.File.Exists(path))
                    System.IO.File.Delete(path);
            }

            _context.Categories.Remove(category);
            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }



        // Orders Management
        public async Task<IActionResult> Orders()
        {
            var orders = await _context.Orders
                .Include(o => o.User)
                .Include(o => o.Items)
                .ThenInclude(oi => oi.Product)
                .OrderByDescending(o => o.OrderDate)
                .ToListAsync();
            return View(orders);
        }

        public async Task<IActionResult> OrderDetails(int id)
        {
            var order = await _context.Orders
                .Include(o => o.User)
                .Include(o => o.Items)
                .ThenInclude(oi => oi.Product)
                .FirstOrDefaultAsync(o => o.Id == id);
                
            if (order == null) return NotFound();
            return PartialView("_OrderDetails", order);
        }

        public async Task<IActionResult> ProductDetails(int id)
        {
            var product = await _context.Products
                .Include(p => p.Category)
                .Include(p => p.Sizes)
                .Include(p => p.Images)
                .FirstOrDefaultAsync(p => p.Id == id);
                
            if (product == null) return NotFound();
            return PartialView("_ProductDetails", product);
        }

        [HttpPost]
        public async Task<IActionResult> UpdateOrderStatus(int orderId, string status)
        {
            var order = await _context.Orders.FindAsync(orderId);
            if (order != null)
            {
                if (Enum.TryParse<OrderStatus>(status, out var orderStatus))
                {
                    order.Status = orderStatus;
                    _context.Orders.Update(order);
                    await _context.SaveChangesAsync();
                    return Json(new { success = true });
                }
            }
            return Json(new { success = false, message = "Invalid order or status" });
        }

        private async Task HandleCoverImageUpload(int productId, IFormFile coverImage)
        {
            var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "products");
            Directory.CreateDirectory(uploadsFolder);

            if (coverImage.Length > 0)
            {
                var fileName = $"cover_{productId}_{Guid.NewGuid()}{Path.GetExtension(coverImage.FileName)}";
                var filePath = Path.Combine(uploadsFolder, fileName);
                
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await coverImage.CopyToAsync(stream);
                }
                
                // Save cover image info to database
                var productImage = new ProductImage
                {
                    ProductId = productId,
                    ImageUrl = $"/images/products/{fileName}",
                    IsCover = true
                };
                
                _context.ProductImages.Add(productImage);
                await _context.SaveChangesAsync();
            }
        }

        private async Task HandleAdditionalImagesUpload(int productId, List<IFormFile> additionalImages)
        {
            var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "products");
            Directory.CreateDirectory(uploadsFolder);

            foreach (var image in additionalImages)
            {
                if (image.Length > 0)
                {
                    var fileName = $"additional_{productId}_{Guid.NewGuid()}{Path.GetExtension(image.FileName)}";
                    var filePath = Path.Combine(uploadsFolder, fileName);
                    
                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await image.CopyToAsync(stream);
                    }
                    
                    // Save additional image info to database
                    var productImage = new ProductImage
                    {
                        ProductId = productId,
                        ImageUrl = $"/images/products/{fileName}",
                        IsCover = false
                    };
                    
                    _context.ProductImages.Add(productImage);
                }
            }
            
            await _context.SaveChangesAsync();
        }

        private async Task HandleImageUploads(int productId, List<IFormFile> images)
        {
            var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "products");
            Directory.CreateDirectory(uploadsFolder);

            foreach (var image in images)
            {
                if (image.Length > 0)
                {
                    var fileName = $"{productId}_{Guid.NewGuid()}{Path.GetExtension(image.FileName)}";
                    var filePath = Path.Combine(uploadsFolder, fileName);
                    
                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await image.CopyToAsync(stream);
                    }
                    
                    // Save image info to database
                    var productImage = new ProductImage
                    {
                        ProductId = productId,
                        ImageUrl = $"/images/products/{fileName}",
                        IsCover = false // First image will be set as cover manually
                    };
                    
                    _context.ProductImages.Add(productImage);
                }
            }
            
            await _context.SaveChangesAsync();
        }





        //hero

        //UploadImage



        public IActionResult CreateHeroImage()
        {
            return View();
        }
        [HttpPost]
        public async Task<IActionResult> CreateHeroImage(HeroImageViewModel model)
        {
            if (model.ImageFile != null && model.ImageFile.Length > 0)
            {
                string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "images", "hero");
                Directory.CreateDirectory(uploadsFolder);

                string fileName = Guid.NewGuid().ToString() + Path.GetExtension(model.ImageFile.FileName);
                string filePath = Path.Combine(uploadsFolder, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await model.ImageFile.CopyToAsync(stream);
                }

                var heroImage = new HeroImage
                {
                    ImageURL = $"/images/hero/{fileName}"
                };

                _context.HeroImages.Add(heroImage);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "Hero image uploaded successfully!";
                return RedirectToAction("Dashboard");
            }

            TempData["ErrorMessage"] = "Please upload a valid image.";
            return View(model);
        }
        public async Task<IActionResult> EditHeroImage()
        {
            var heroImage = await _context.HeroImages.FirstOrDefaultAsync();
            if (heroImage == null) return RedirectToAction("CreateHeroImage");

            var model = new HeroImageViewModel
            {
                ExistingImageUrl = heroImage.ImageURL
            };

            return View(model);
        }
        [HttpPost]
        public async Task<IActionResult> EditHeroImage(HeroImageViewModel model)
        {
            var heroImage = await _context.HeroImages.FirstOrDefaultAsync();
            if (heroImage == null) return NotFound();

            if (model.ImageFile != null && model.ImageFile.Length > 0)
            {
                // Delete old image file
                if (!string.IsNullOrEmpty(heroImage.ImageURL))
                {
                    string oldPath = Path.Combine(_webHostEnvironment.WebRootPath, heroImage.ImageURL.TrimStart('/'));
                    if (System.IO.File.Exists(oldPath))
                    {
                        System.IO.File.Delete(oldPath);
                    }
                }

                string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "images", "hero");
                Directory.CreateDirectory(uploadsFolder);

                string fileName = Guid.NewGuid().ToString() + Path.GetExtension(model.ImageFile.FileName);
                string filePath = Path.Combine(uploadsFolder, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await model.ImageFile.CopyToAsync(stream);
                }

                heroImage.ImageURL = $"/images/hero/{fileName}";
                _context.HeroImages.Update(heroImage);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "Hero image updated successfully!";
                return RedirectToAction("Dashboard");
            }

            TempData["ErrorMessage"] = "Please upload a valid image.";
            model.ExistingImageUrl = heroImage.ImageURL;
            return View(model);
        }







        //public IActionResult Create()
        //{
        //    return View();
        //}

        //[HttpPost]
        //public async Task<IActionResult> Create(HeroImageViewModel model)
        //{
        //    if (model.ImageFile != null && model.ImageFile.Length > 0)
        //    {
        //        // 1. «Õ›Ÿ «·’Ê—… ⁄·Ï «·”Ì—›—
        //        var fileName = Guid.NewGuid().ToString() + Path.GetExtension(model.ImageFile.FileName);
        //        var filePath = Path.Combine("wwwroot/images/hero", fileName);
        //        using (var stream = new FileStream(filePath, FileMode.Create))
        //        {
        //            await model.ImageFile.CopyToAsync(stream);
        //        }

        //        // 2. Œ“¯‰ «·‹ URL ›Ì «·œ« «»Ì“
        //        var heroImage = new HeroImage
        //        {
        //            ImageURL = "/images/hero/" + fileName
        //        };

        //        _context.HeroImages.Add(heroImage);
        //        await _context.SaveChangesAsync();

        //        return RedirectToAction("Dashboard"); // √Ê √Ì ’›Õ…  «‰Ì…
        //    }

        //    return View(model);
        //}
        //public IActionResult Edit(int id)
        //{
        //    var heroImage = _context.HeroImages.FirstOrDefault(h => h.Id == id);
        //    if (heroImage == null) return NotFound();

        //    var viewModel = new HeroImageViewModel
        //    {
        //        ExistingImageURL = heroImage.ImageURL
        //    };

        //    return View(viewModel);
        //}

        //// --- POST: Edit
        //[HttpPost]
        //public async Task<IActionResult> Edit(int id, HeroImageViewModel model)
        //{
        //    var heroImage = _context.HeroImages.FirstOrDefault(h => h.Id == id);
        //    if (heroImage == null) return NotFound();

        //    if (model.ImageFile != null && model.ImageFile.Length > 0)
        //    {
        //        // 1. Õ–› «·’Ê—… «·ﬁœÌ„… „‰ «·”Ì—›—
        //        var oldImagePath = Path.Combine(_webHostEnvironment.WebRootPath, heroImage.ImageURL.TrimStart('/'));
        //        if (System.IO.File.Exists(oldImagePath))
        //        {
        //            System.IO.File.Delete(oldImagePath);
        //        }

        //        // 2. —›⁄ «·’Ê—… «·ÃœÌœ…
        //        var newPath = Path.Combine("images", "hero", Guid.NewGuid() + Path.GetExtension(model.ImageFile.FileName));
        //        var newFullPath = Path.Combine(_webHostEnvironment.WebRootPath, newPath);

        //        Directory.CreateDirectory(Path.GetDirectoryName(newFullPath));
        //        using (var stream = new FileStream(newFullPath, FileMode.Create))
        //        {
        //            await model.ImageFile.CopyToAsync(stream);
        //        }

        //        // 3. Õ›Ÿ «·—«»ÿ «·ÃœÌœ ›Ì «·œ« «»Ì“
        //        heroImage.ImageURL = "/" + newPath.Replace("\\", "/");
        //        _context.HeroImages.Update(heroImage);
        //        await _context.SaveChangesAsync();

        //        return RedirectToAction("Dashboard");
        //    }

        //    return View(model);
        //}


        //[HttpGet]
        //public IActionResult AddHeroImage()
        //{
        //    return View();
        //}

        //[HttpPost]
        //public async Task<IActionResult> AddHeroImage(HeroImageViewModel model)
        //{
        //    if (model.ImageFile != null && model.ImageFile.Length > 0)
        //    {
        //        var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/uploads");
        //        if (!Directory.Exists(uploadsFolder))
        //            Directory.CreateDirectory(uploadsFolder);

        //        var uniqueFileName = Guid.NewGuid().ToString() + "_" + model.ImageFile.FileName;
        //        var filePath = Path.Combine(uploadsFolder, uniqueFileName);

        //        using (var stream = new FileStream(filePath, FileMode.Create))
        //        {
        //            await model.ImageFile.CopyToAsync(stream);
        //        }

        //        var heroImage = new HeroImage
        //        {
        //            ImageURL = "/uploads/" + uniqueFileName
        //        };

        //        // «Õ›Ÿ ›Ì ﬁ«⁄œ… «·»Ì«‰« 
        //        _context.HeroImages.Add(heroImage);
        //        await _context.SaveChangesAsync();

        //        return RedirectToAction("Dashboard"); // √Ê √Ì View √Œ—Ï
        //    }

        //    ModelState.AddModelError("", "Please upload a valid image.");
        //    return View(model);
        //}


        //[HttpGet]
        //public IActionResult EditHeroImage(int id)
        //{
        //    var heroImage = _context.HeroImages.Find(id);
        //    if (heroImage == null)
        //        return NotFound();

        //    var viewModel = new HeroImageViewModel
        //    {
        //        ExistingImageUrl = heroImage.ImageURL
        //    };

        //    return View(viewModel);
        //}

        //[HttpPost]
        //public async Task<IActionResult> EditHeroImage(int id, HeroImageViewModel model)
        //{
        //    var heroImage = _context.HeroImages.Find(id);
        //    if (heroImage == null)
        //        return NotFound();

        //    if (model.ImageFile != null && model.ImageFile.Length > 0)
        //    {
        //        // Õ–› «·’Ê—… «·ﬁœÌ„…
        //        if (!string.IsNullOrEmpty(heroImage.ImageURL))
        //        {
        //            var oldPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", heroImage.ImageURL.TrimStart('/'));
        //            if (System.IO.File.Exists(oldPath))
        //                System.IO.File.Delete(oldPath);
        //        }

        //        // —›⁄ «·ÃœÌœ…
        //        var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/uploads");
        //        var uniqueFileName = Guid.NewGuid().ToString() + "_" + model.ImageFile.FileName;
        //        var filePath = Path.Combine(uploadsFolder, uniqueFileName);

        //        using (var stream = new FileStream(filePath, FileMode.Create))
        //        {
        //            await model.ImageFile.CopyToAsync(stream);
        //        }

        //        heroImage.ImageURL = "/uploads/" + uniqueFileName;
        //        await _context.SaveChangesAsync();

        //        return RedirectToAction("Dashboard");
        //    }

        //    ModelState.AddModelError("", "Please upload a valid image.");
        //    return View(model);
        //}





    }
}

