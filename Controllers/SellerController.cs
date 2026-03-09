using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ArabianHorseSystem.Data;
using ArabianHorseSystem.Models;
using System.Security.Claims;
using System.Threading.Tasks;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;

namespace ArabianHorseSystem.Controllers
{
    [Authorize(Policy = "SellerOnly")]
    [ApiController]
    [Route("api/[controller]")]
    public class SellerController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;

        public SellerController(ApplicationDbContext context, IWebHostEnvironment environment)
        {
            _context = context;
            _environment = environment;
        }

        // GET: api/seller/dashboard
        [HttpGet("dashboard")]
        public async Task<IActionResult> GetDashboard()
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            
            // Get seller's owner profile
            var owner = await _context.Owners
                .Include(o => o.User)
                .FirstOrDefaultAsync(o => o.UserId == userId);

            if (owner == null)
                return NotFound("Seller profile not found");

            // Get seller's horses
            var horses = await _context.HorseProfiles
                .Where(h => h.OwnerId == owner.OwnerId)
                .Include(h => h.Auctions)
                .OrderByDescending(h => h.CreatedAt)
                .Select(h => new
                {
                    h.MicrochipId,
                    h.Name,
                    h.Breed,
                    h.Age,
                    h.Gender,
                    h.Color,
                    h.Height,
                    h.Weight,
                    h.ImageUrl,
                    h.IsForSale,
                    h.Price,
                    h.Status,
                    AuctionCount = h.Auctions.Count,
                    ActiveAuctions = h.Auctions.Count(a => a.Status == "Live" || a.Status == "Upcoming")
                })
                .ToListAsync();

            // Get seller's auctions
            var auctions = await _context.Auctions
                .Where(a => a.CreatedById == userId)
                .Include(a => a.Horse)
                .Include(a => a.Bids)
                .OrderByDescending(a => a.StartTime)
                .Select(a => new
                {
                    a.AuctionId,
                    a.Name,
                    a.StartTime,
                    a.EndTime,
                    a.BasePrice,
                    a.CurrentBid,
                    a.Status,
                    a.ImageUrl,
                    HorseName = a.Horse.Name,
                    BidCount = a.Bids.Count,
                    HighestBid = a.Bids.OrderByDescending(b => b.Amount).Select(b => b.Amount).FirstOrDefault(),
                    TimeRemaining = a.EndTime > DateTime.UtcNow ? a.EndTime - DateTime.UtcNow : TimeSpan.Zero
                })
                .ToListAsync();

            // Get pending approvals
            var pendingApprovals = horses.Count(h => h.Status == "Pending");

            // Get completed sales
            var completedSales = auctions.Count(a => a.Status == "Completed");

            return Ok(new
            {
                TotalHorses = horses.Count,
                PendingApprovals = pendingApprovals,
                ActiveAuctions = auctions.Count(a => a.Status == "Live" || a.Status == "Upcoming"),
                CompletedSales = completedSales,
                TotalRevenue = auctions.Where(a => a.Status == "Completed").Sum(a => a.HighestBid ?? 0),
                Horses = horses,
                Auctions = auctions
            });
        }

        // POST: api/seller/add-horse
        [HttpPost("add-horse")]
        public async Task<IActionResult> AddHorse([FromForm] AddHorseDto dto)
        {
            try
            {
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                
                // Get or create owner profile
                var owner = await _context.Owners
                    .FirstOrDefaultAsync(o => o.UserId == userId);

                if (owner == null)
                {
                    // Create owner profile if not exists
                    var user = await _context.Users.FindAsync(userId);
                    owner = new Owner
                    {
                        UserId = userId,
                        OwnerName = user.FullName,
                        Email = user.Email,
                        Phone = user.PhoneNumber,
                        IsVerified = false // Needs admin verification
                    };
                    _context.Owners.Add(owner);
                    await _context.SaveChangesAsync();
                }

                // Check if horse with same microchip already exists
                var existingHorse = await _context.HorseProfiles
                    .FirstOrDefaultAsync(h => h.MicrochipId == dto.MicrochipId);
                
                if (existingHorse != null)
                    return BadRequest("Horse with this microchip ID already exists");

                // Handle image upload
                string? imageUrl = null;
                if (dto.ImageFile != null && dto.ImageFile.Length > 0)
                {
                    // Validate file type
                    var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif" };
                    var fileExtension = Path.GetExtension(dto.ImageFile.FileName).ToLowerInvariant();
                    
                    if (!allowedExtensions.Contains(fileExtension))
                        return BadRequest("Only image files (jpg, jpeg, png, gif) are allowed");

                    // Validate file size (max 5MB)
                    if (dto.ImageFile.Length > 5 * 1024 * 1024)
                        return BadRequest("Image file size cannot exceed 5MB");

                    var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", "horses");
                    if (!Directory.Exists(uploadsFolder))
                        Directory.CreateDirectory(uploadsFolder);

                    var fileName = $"horse_{Guid.NewGuid()}{fileExtension}";
                    var filePath = Path.Combine(uploadsFolder, fileName);
                    
                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await dto.ImageFile.CopyToAsync(stream);
                    }
                    
                    imageUrl = $"/uploads/horses/{fileName}";
                }

                // Handle pedigree document upload
                string? pedigreeUrl = null;
                if (dto.PedigreeFile != null && dto.PedigreeFile.Length > 0)
                {
                    var allowedExtensions = new[] { ".pdf", ".doc", ".docx", ".jpg", ".jpeg", ".png" };
                    var fileExtension = Path.GetExtension(dto.PedigreeFile.FileName).ToLowerInvariant();
                    
                    if (!allowedExtensions.Contains(fileExtension))
                        return BadRequest("Only PDF, DOC, DOCX, and image files are allowed for pedigree");

                    if (dto.PedigreeFile.Length > 10 * 1024 * 1024) // 10MB max
                        return BadRequest("Pedigree file size cannot exceed 10MB");

                    var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", "pedigrees");
                    if (!Directory.Exists(uploadsFolder))
                        Directory.CreateDirectory(uploadsFolder);

                    var fileName = $"pedigree_{Guid.NewGuid()}{fileExtension}";
                    var filePath = Path.Combine(uploadsFolder, fileName);
                    
                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await dto.PedigreeFile.CopyToAsync(stream);
                    }
                    
                    pedigreeUrl = $"/uploads/pedigrees/{fileName}";
                }

                var horse = new HorseProfile
                {
                    MicrochipId = dto.MicrochipId,
                    Name = dto.Name,
                    Breed = dto.Breed,
                    Age = dto.Age,
                    Gender = dto.Gender,
                    Color = dto.Color,
                    Height = dto.Height,
                    Weight = dto.Weight,
                    Description = dto.Description,
                    Price = dto.Price,
                    IsForSale = dto.IsForSale,
                    ImageUrl = imageUrl,
                    PedigreeUrl = pedigreeUrl,
                    OwnerId = owner.OwnerId,
                    Status = "Pending", // Needs admin approval
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _context.HorseProfiles.Add(horse);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    message = "Horse added successfully and pending approval",
                    horseId = horse.MicrochipId,
                    imageUrl = imageUrl,
                    pedigreeUrl = pedigreeUrl
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An error occurred while adding the horse", error = ex.Message });
            }
        }

        // PUT: api/seller/update-horse/{microchipId}
        [HttpPut("update-horse/{microchipId}")]
        public async Task<IActionResult> UpdateHorse(string microchipId, [FromForm] UpdateHorseDto dto)
        {
            try
            {
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                
                var horse = await _context.HorseProfiles
                    .Include(h => h.Owner)
                    .FirstOrDefaultAsync(h => h.MicrochipId == microchipId);

                if (horse == null)
                    return NotFound("Horse not found");

                // Verify ownership
                if (horse.Owner.UserId != userId && !User.IsInRole("Admin"))
                    return Forbid("You can only update your own horses");

                // Only allow updates if horse is not in an active auction
                var hasActiveAuction = await _context.Auctions
                    .AnyAsync(a => a.MicrochipId == microchipId && 
                           (a.Status == "Live" || a.Status == "Upcoming"));

                if (hasActiveAuction)
                    return BadRequest("Cannot update horse while it has active auctions");

                // Handle new image upload
                if (dto.ImageFile != null && dto.ImageFile.Length > 0)
                {
                    // Validate and save new image
                    var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif" };
                    var fileExtension = Path.GetExtension(dto.ImageFile.FileName).ToLowerInvariant();
                    
                    if (!allowedExtensions.Contains(fileExtension))
                        return BadRequest("Only image files are allowed");

                    if (dto.ImageFile.Length > 5 * 1024 * 1024)
                        return BadRequest("Image file size cannot exceed 5MB");

                    // Delete old image if exists
                    if (!string.IsNullOrEmpty(horse.ImageUrl))
                    {
                        var oldImagePath = Path.Combine(_environment.WebRootPath, horse.ImageUrl.TrimStart('/'));
                        if (System.IO.File.Exists(oldImagePath))
                            System.IO.File.Delete(oldImagePath);
                    }

                    var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", "horses");
                    if (!Directory.Exists(uploadsFolder))
                        Directory.CreateDirectory(uploadsFolder);

                    var fileName = $"horse_{Guid.NewGuid()}{fileExtension}";
                    var filePath = Path.Combine(uploadsFolder, fileName);
                    
                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await dto.ImageFile.CopyToAsync(stream);
                    }
                    
                    horse.ImageUrl = $"/uploads/horses/{fileName}";
                }

                // Update fields
                if (!string.IsNullOrEmpty(dto.Name)) horse.Name = dto.Name;
                if (!string.IsNullOrEmpty(dto.Breed)) horse.Breed = dto.Breed;
                if (dto.Age.HasValue) horse.Age = dto.Age.Value;
                if (!string.IsNullOrEmpty(dto.Gender)) horse.Gender = dto.Gender;
                if (!string.IsNullOrEmpty(dto.Color)) horse.Color = dto.Color;
                if (dto.Height.HasValue) horse.Height = dto.Height.Value;
                if (dto.Weight.HasValue) horse.Weight = dto.Weight.Value;
                if (!string.IsNullOrEmpty(dto.Description)) horse.Description = dto.Description;
                if (dto.Price.HasValue) horse.Price = dto.Price.Value;
                if (dto.IsForSale.HasValue) horse.IsForSale = dto.IsForSale.Value;

                horse.UpdatedAt = DateTime.UtcNow;
                
                // If major changes, set status back to pending for review
                if (dto.Price.HasValue || !string.IsNullOrEmpty(dto.Name) || !string.IsNullOrEmpty(dto.Breed))
                {
                    horse.Status = "Pending";
                }

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    message = "Horse updated successfully",
                    horseId = horse.MicrochipId,
                    status = horse.Status
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An error occurred while updating the horse", error = ex.Message });
            }
        }

        // DELETE: api/seller/delete-horse/{microchipId}
        [HttpDelete("delete-horse/{microchipId}")]
        public async Task<IActionResult> DeleteHorse(string microchipId)
        {
            try
            {
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                
                var horse = await _context.HorseProfiles
                    .Include(h => h.Owner)
                    .Include(h => h.Auctions)
                    .FirstOrDefaultAsync(h => h.MicrochipId == microchipId);

                if (horse == null)
                    return NotFound("Horse not found");

                // Verify ownership
                if (horse.Owner.UserId != userId && !User.IsInRole("Admin"))
                    return Forbid("You can only delete your own horses");

                // Check for active auctions
                var hasActiveAuction = horse.Auctions.Any(a => a.Status == "Live" || a.Status == "Upcoming");
                if (hasActiveAuction)
                    return BadRequest("Cannot delete horse while it has active auctions");

                // Check for completed auctions
                var hasCompletedAuctions = horse.Auctions.Any(a => a.Status == "Completed");
                if (hasCompletedAuctions)
                    return BadRequest("Cannot delete horse with completed auctions");

                // Delete associated images
                if (!string.IsNullOrEmpty(horse.ImageUrl))
                {
                    var imagePath = Path.Combine(_environment.WebRootPath, horse.ImageUrl.TrimStart('/'));
                    if (System.IO.File.Exists(imagePath))
                        System.IO.File.Delete(imagePath);
                }

                if (!string.IsNullOrEmpty(horse.PedigreeUrl))
                {
                    var pedigreePath = Path.Combine(_environment.WebRootPath, horse.PedigreeUrl.TrimStart('/'));
                    if (System.IO.File.Exists(pedigreePath))
                        System.IO.File.Delete(pedigreePath);
                }

                _context.HorseProfiles.Remove(horse);
                await _context.SaveChangesAsync();

                return Ok(new { message = "Horse deleted successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An error occurred while deleting the horse", error = ex.Message });
            }
        }

        // GET: api/seller/my-horses
        [HttpGet("my-horses")]
        public async Task<IActionResult> GetMyHorses([FromQuery] string? status = null)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            
            var owner = await _context.Owners
                .FirstOrDefaultAsync(o => o.UserId == userId);

            if (owner == null)
                return Ok(new List<object>()); // Return empty list if no owner profile

            var query = _context.HorseProfiles
                .Where(h => h.OwnerId == owner.OwnerId)
                .Include(h => h.Auctions)
                .AsQueryable();

            if (!string.IsNullOrEmpty(status))
            {
                query = query.Where(h => h.Status == status);
            }

            var horses = await query
                .OrderByDescending(h => h.CreatedAt)
                .Select(h => new
                {
                    h.MicrochipId,
                    h.Name,
                    h.Breed,
                    h.Age,
                    h.Gender,
                    h.Color,
                    h.ImageUrl,
                    h.Price,
                    h.IsForSale,
                    h.Status,
                    h.CreatedAt,
                    h.UpdatedAt,
                    Auctions = h.Auctions.Select(a => new
                    {
                        a.AuctionId,
                        a.Name,
                        a.Status,
                        a.StartTime,
                        a.EndTime,
                        a.CurrentBid
                    })
                })
                .ToListAsync();

            return Ok(horses);
        }

        // GET: api/seller/horse/{microchipId}
        [HttpGet("horse/{microchipId}")]
        public async Task<IActionResult> GetHorseDetails(string microchipId)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            
            var horse = await _context.HorseProfiles
                .Include(h => h.Owner)
                .Include(h => h.Auctions)
                    .ThenInclude(a => a.Bids)
                        .ThenInclude(b => b.Bidder)
                .FirstOrDefaultAsync(h => h.MicrochipId == microchipId);

            if (horse == null)
                return NotFound("Horse not found");

            // Verify ownership
            if (horse.Owner.UserId != userId && !User.IsInRole("Admin"))
                return Forbid();

            return Ok(new
            {
                horse.MicrochipId,
                horse.Name,
                horse.Breed,
                horse.Age,
                horse.Gender,
                horse.Color,
                horse.Height,
                horse.Weight,
                horse.Description,
                horse.Price,
                horse.IsForSale,
                horse.ImageUrl,
                horse.PedigreeUrl,
                horse.Status,
                horse.CreatedAt,
                horse.UpdatedAt,
                Auctions = horse.Auctions.Select(a => new
                {
                    a.AuctionId,
                    a.Name,
                    a.Status,
                    a.StartTime,
                    a.EndTime,
                    a.BasePrice,
                    a.CurrentBid,
                    BidCount = a.Bids.Count,
                    Bids = a.Bids.OrderByDescending(b => b.Amount).Take(5).Select(b => new
                    {
                        b.Amount,
                        b.Timestamp,
                        BidderName = b.Bidder.FullName
                    })
                })
            });
        }

        // GET: api/seller/my-auctions
        [HttpGet("my-auctions")]
        public async Task<IActionResult> GetMyAuctions([FromQuery] string? status = null)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            
            var query = _context.Auctions
                .Where(a => a.CreatedById == userId)
                .Include(a => a.Horse)
                .Include(a => a.Bids)
                .AsQueryable();

            if (!string.IsNullOrEmpty(status))
            {
                query = query.Where(a => a.Status == status);
            }

            var auctions = await query
                .OrderByDescending(a => a.StartTime)
                .Select(a => new
                {
                    a.AuctionId,
                    a.Name,
                    a.StartTime,
                    a.EndTime,
                    a.BasePrice,
                    a.CurrentBid,
                    a.Status,
                    a.ImageUrl,
                    a.VideoUrl,
                    HorseName = a.Horse.Name,
                    HorseImage = a.Horse.ImageUrl,
                    BidCount = a.Bids.Count,
                    HighestBid = a.Bids.OrderByDescending(b => b.Amount).Select(b => b.Amount).FirstOrDefault(),
                    TimeRemaining = a.EndTime > DateTime.UtcNow ? a.EndTime - DateTime.UtcNow : TimeSpan.Zero
                })
                .ToListAsync();

            return Ok(auctions);
        }

        // POST: api/seller/withdraw-auction/{auctionId}
        [HttpPost("withdraw-auction/{auctionId}")]
        public async Task<IActionResult> WithdrawAuction(int auctionId)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            
            var auction = await _context.Auctions
                .Include(a => a.Bids)
                .FirstOrDefaultAsync(a => a.AuctionId == auctionId);

            if (auction == null)
                return NotFound("Auction not found");

            // Verify ownership
            if (auction.CreatedById != userId && !User.IsInRole("Admin"))
                return Forbid();

            // Check if auction can be withdrawn
            if (auction.Status != "Upcoming")
                return BadRequest("Only upcoming auctions can be withdrawn");

            if (auction.Bids.Any())
                return BadRequest("Cannot withdraw auction with existing bids");

            auction.Status = "Withdrawn";
            await _context.SaveChangesAsync();

            return Ok(new { message = "Auction withdrawn successfully" });
        }

        // GET: api/seller/notifications
        [HttpGet("notifications")]
        public async Task<IActionResult> GetNotifications()
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            
            // Get recent notifications for the seller
            var notifications = new List<object>();

            // Get pending horse approvals
            var pendingHorses = await _context.HorseProfiles
                .Where(h => h.Owner.UserId == userId && h.Status == "Pending")
                .CountAsync();

            if (pendingHorses > 0)
            {
                notifications.Add(new
                {
                    Type = "HorseApproval",
                    Message = $"You have {pendingHorses} horse(s) pending approval",
                    Count = pendingHorses,
                    Timestamp = DateTime.UtcNow
                });
            }

            // Get ended auctions waiting for action
            var endedAuctions = await _context.Auctions
                .Where(a => a.CreatedById == userId && 
                       a.Status == "WaitingForSeller" &&
                       a.EndTime <= DateTime.UtcNow)
                .Include(a => a.Bids)
                .Select(a => new
                {
                    a.AuctionId,
                    a.Name,
                    HighestBid = a.Bids.OrderByDescending(b => b.Amount).FirstOrDefault().Amount
                })
                .ToListAsync();

            foreach (var auction in endedAuctions)
            {
                notifications.Add(new
                {
                    Type = "AuctionEnded",
                    Message = $"Auction '{auction.Name}' has ended with highest bid of ${auction.HighestBid}",
                    AuctionId = auction.AuctionId,
                    Timestamp = DateTime.UtcNow
                });
            }

            // Get live auctions
            var liveAuctions = await _context.Auctions
                .Where(a => a.CreatedById == userId && a.Status == "Live")
                .CountAsync();

            if (liveAuctions > 0)
            {
                notifications.Add(new
                {
                    Type = "LiveAuctions",
                    Message = $"You have {liveAuctions} live auction(s) running",
                    Count = liveAuctions,
                    Timestamp = DateTime.UtcNow
                });
            }

            return Ok(notifications.OrderByDescending(n => ((dynamic)n).Timestamp));
        }
    }

    // DTO Classes
    public class AddHorseDto
    {
        public string MicrochipId { get; set; }
        public string Name { get; set; }
        public string Breed { get; set; }
        public int Age { get; set; }
        public string Gender { get; set; }
        public string Color { get; set; }
        public decimal? Height { get; set; }
        public decimal? Weight { get; set; }
        public string? Description { get; set; }
        public decimal? Price { get; set; }
        public bool IsForSale { get; set; } = true;
        public IFormFile? ImageFile { get; set; }
        public IFormFile? PedigreeFile { get; set; }
    }

    public class UpdateHorseDto
    {
        public string? Name { get; set; }
        public string? Breed { get; set; }
        public int? Age { get; set; }
        public string? Gender { get; set; }
        public string? Color { get; set; }
        public decimal? Height { get; set; }
        public decimal? Weight { get; set; }
        public string? Description { get; set; }
        public decimal? Price { get; set; }
        public bool? IsForSale { get; set; }
        public IFormFile? ImageFile { get; set; }
    }
}
