using NileJewal.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace NileJewal.Data
{
    public static class DbInitializer
    {
        public static async Task SeedRolesAndAdminAsync(IServiceProvider serviceProvider)
        {
            var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var context = serviceProvider.GetRequiredService<ApplicationDbContext>();

            // 1. إنشاء الأدوار
            string[] roleNames = { "Admin", "Receptionist" };
            foreach (var roleName in roleNames)
            {
                if (!await roleManager.RoleExistsAsync(roleName))
                {
                    await roleManager.CreateAsync(new IdentityRole(roleName));
                }
            }

            // 2. إنشاء حساب الأدمن
            var adminEmail = "admin@hotel.com";
            var adminUser = await userManager.FindByEmailAsync(adminEmail);

            if (adminUser == null)
            {
                var newAdmin = new ApplicationUser
                {
                    UserName = adminEmail,
                    Email = adminEmail,
                    FullName = "مدير النظام الرئيسي",
                    EmailConfirmed = true
                };

                var createResult = await userManager.CreateAsync(newAdmin, "Admin@123");
                if (createResult.Succeeded)
                {
                    await userManager.AddToRoleAsync(newAdmin, "Admin");
                }
            }

            // 3. إنشاء الغرف تلقائياً (من 401 إلى 433) إذا لم تكن موجودة
            if (!await context.Rooms.AnyAsync())
            {
                var rooms = new List<Room>();
                for (int i = 401; i <= 433; i++)
                {
                    rooms.Add(new Room
                    {
                        RoomNumber = i.ToString(),
                        Floor = 4,
                        Type = RoomType.Single,
                        IsActive = true
                    });
                }
                await context.Rooms.AddRangeAsync(rooms);
                await context.SaveChangesAsync();
            }
        }
    }
}