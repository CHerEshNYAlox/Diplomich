using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Google.Cloud.Firestore;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using DocSystemWeb.Models;

namespace DocSystemWeb.Controllers
{
    [Authorize]
    public class AccountController : Controller
    {
        private readonly FirestoreDb _db;
        private readonly PasswordHasher<string> _passwordHasher;

        public AccountController(FirestoreDb db)
        {
            _db = db;
            _passwordHasher = new PasswordHasher<string>();
        }

        [AllowAnonymous]
        [HttpGet]
        public async Task<IActionResult> Login()
        {
            if (User.Identity.IsAuthenticated) return RedirectToAction("Index", "Document");

            // АВТОМАТИЧЕСКАЯ ГЕНЕРАЦИЯ АДМИНА ПРИ ПЕРВОМ ОТКРЫТИИ СТРАНИЦЫ
            CollectionReference usersRef = _db.Collection("Users");
            QuerySnapshot snapshot = await usersRef.WhereEqualTo("Login", "admin").GetSnapshotAsync();

            if (snapshot.Documents.Count == 0)
            {
                var adminUser = new UserModel
                {
                    Login = "admin",
                    Password = _passwordHasher.HashPassword(null, "admin"),
                    Role = "Admin",
                    FullName = "Иван Иванов",
                    Position = "Главный администратор"
                };
                await usersRef.AddAsync(adminUser);
            }

            return View();
        }

        [AllowAnonymous]
        [HttpPost]
        public async Task<IActionResult> Login(string login, string password)
        {
            CollectionReference usersRef = _db.Collection("Users");
            QuerySnapshot snapshot = await usersRef.WhereEqualTo("Login", login).GetSnapshotAsync();

            if (snapshot.Documents.Count > 0)
            {
                var userDoc = snapshot.Documents[0];
                var user = userDoc.ConvertTo<UserModel>();

                var result = _passwordHasher.VerifyHashedPassword(null, user.Password, password);

                if (result == PasswordVerificationResult.Success)
                {
                    var claims = new List<Claim>
                    {
                        new Claim(ClaimTypes.Name, user.Login),
                        new Claim("FullName", user.FullName ?? user.Login),
                        new Claim(ClaimTypes.Role, user.Role ?? "User"),
                        new Claim("Position", user.Position ?? "Сотрудник")
                    };

                    var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                    await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(claimsIdentity));

                    return RedirectToAction("Index", "Document");
                }
            }

            ViewBag.Error = "Неверный логин или пароль";
            return View();
        }

        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login");
        }

        [Authorize(Roles = "Admin,Manager")]
        [HttpGet]
        public async Task<IActionResult> UsersList()
        {
            QuerySnapshot snapshot = await _db.Collection("Users").GetSnapshotAsync();
            var users = new List<UserModel>();

            foreach (var doc in snapshot.Documents)
            {
                var user = doc.ConvertTo<UserModel>();
                user.Id = doc.Id;
                users.Add(user);
            }

            return View(users);
        }

        [Authorize(Roles = "Admin,Manager")]
        [HttpPost]
        public async Task<IActionResult> AddUser(UserModel model, string Password)
        {
            if (User.IsInRole("Manager") && model.Role == "Admin")
            {
                TempData["ErrorNotification"] = "Ошибка: Руководитель не имеет права назначать роль Администратора.";
                return RedirectToAction("UsersList");
            }

            if (string.IsNullOrEmpty(model.Role)) model.Role = "User";

            CollectionReference usersRef = _db.Collection("Users");
            QuerySnapshot snapshot = await usersRef.WhereEqualTo("Login", model.Login).GetSnapshotAsync();

            if (snapshot.Documents.Count > 0)
            {
                TempData["ErrorNotification"] = "Пользователь с таким логином уже существует.";
                return RedirectToAction("UsersList");
            }

            model.Password = _passwordHasher.HashPassword(null, Password);

            await usersRef.AddAsync(model);
            TempData["SuccessNotification"] = $"Сотрудник {model.FullName} успешно добавлен.";
            return RedirectToAction("UsersList");
        }

        [Authorize(Roles = "Admin")]
        [HttpGet]
        public async Task<IActionResult> DeleteUser(string id)
        {
            DocumentReference docRef = _db.Collection("Users").Document(id);
            DocumentSnapshot doc = await docRef.GetSnapshotAsync();

            if (doc.Exists)
            {
                var user = doc.ConvertTo<UserModel>();
                // Дополнительная защита на бэкенде от удаления самого себя
                if (user.Login == User.Identity.Name)
                {
                    TempData["ErrorNotification"] = "Вы не можете удалить свой собственный аккаунт.";
                    return RedirectToAction("UsersList");
                }
            }

            await docRef.DeleteAsync();
            TempData["SuccessNotification"] = "Пользователь удален.";
            return RedirectToAction("UsersList");
        }

        [Authorize(Roles = "Admin")]
        [HttpGet]
        public async Task<IActionResult> EditUser(string id)
        {
            DocumentSnapshot doc = await _db.Collection("Users").Document(id).GetSnapshotAsync();
            if (doc.Exists)
            {
                var user = doc.ConvertTo<UserModel>();
                user.Id = doc.Id;
                return View(user);
            }
            return RedirectToAction("UsersList");
        }

        [Authorize(Roles = "Admin")]
        [HttpPost]
        public async Task<IActionResult> EditUser(UserModel model, string newPassword)
        {
            DocumentReference userRef = _db.Collection("Users").Document(model.Id);
            DocumentSnapshot oldDoc = await userRef.GetSnapshotAsync();

            if (!oldDoc.Exists) return RedirectToAction("UsersList");

            var oldUser = oldDoc.ConvertTo<UserModel>();
            string finalRole = model.Role;

            // БИЗНЕС-ПРАВИЛО: Администратор не должен иметь возможность снимать с себя роль Admin
            if (oldUser.Login == User.Identity.Name && oldUser.Role == "Admin" && model.Role != "Admin")
            {
                finalRole = "Admin"; // Принудительно сохраняем роль Admin
                TempData["ErrorNotification"] = "Изменения сохранены, но роль принудительно оставлена 'Администратор'.";
            }

            var updates = new Dictionary<string, object>
            {
                { "Login", model.Login },
                { "FullName", model.FullName },
                { "Position", model.Position },
                { "Role", finalRole }
            };

            if (!string.IsNullOrWhiteSpace(newPassword))
            {
                updates.Add("Password", _passwordHasher.HashPassword(null, newPassword));
            }

            await userRef.UpdateAsync(updates);

            if (TempData["ErrorNotification"] == null)
            {
                TempData["SuccessNotification"] = $"Данные пользователя {model.FullName} обновлены.";
            }

            return RedirectToAction("UsersList");
        }
    }
}