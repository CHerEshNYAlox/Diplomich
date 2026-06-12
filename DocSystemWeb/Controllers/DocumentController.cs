using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Google.Cloud.Firestore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using System.Text.RegularExpressions;
using DocSystemWeb.Models;

namespace DocSystemWeb.Controllers
{
    [Authorize]
    public class DocumentController : Controller
    {
        private readonly FirestoreDb _db;
        private readonly IWebHostEnvironment _appEnvironment;

        public DocumentController(FirestoreDb db, IWebHostEnvironment appEnvironment)
        {
            _db = db;
            _appEnvironment = appEnvironment;
        }

        private async Task LogAction(string action, string details)
        {
            var log = new AuditLogModel
            {
                UserId = User.Identity.Name,
                UserName = User.FindFirst("FullName")?.Value ?? User.Identity.Name,
                Action = action,
                Details = details,
                Timestamp = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)
            };
            await _db.Collection("AuditLogs").AddAsync(log);
        }

        [HttpGet]
        public async Task<IActionResult> Index(string searchString, string searchCategory, string searchStatus)
        {
            QuerySnapshot snapshot = await _db.Collection("Documents").GetSnapshotAsync();
            var docs = new List<DocumentModel>();

            bool isAdmin = User.IsInRole("Admin");
            bool isManager = User.IsInRole("Manager") || User.IsInRole("Руководитель");
            string currentUser = User.Identity.Name;

            foreach (var docSnap in snapshot.Documents)
            {
                var doc = docSnap.ConvertTo<DocumentModel>();
                doc.Id = docSnap.Id;
                if (string.IsNullOrEmpty(doc.Status)) doc.Status = "Черновик";
                if (doc.Version == 0) doc.Version = 1;
                if (doc.VersionHistory == null) doc.VersionHistory = new List<string>();

                bool isInstruction = doc.Category == "Инструкция";
                bool isOwner = doc.OwnerId == currentUser;
                bool isSharedWithMe = !string.IsNullOrEmpty(doc.SharedWithLogins) &&
                                      doc.SharedWithLogins.Split(',').Select(s => s.Trim().ToLower()).Contains(currentUser.ToLower());

                // Администратор и Руководитель видят всё
                if (isAdmin || isManager || isOwner || isInstruction || isSharedWithMe)
                {
                    docs.Add(doc);
                }
            }

            ViewBag.TotalDocs = docs.Count;
            ViewBag.ApprovedDocs = docs.Count(d => d.Status == "Утверждено");
            ViewBag.DraftDocs = docs.Count(d => d.Status == "Черновик");
            ViewBag.MyDocs = docs.Count(d => d.OwnerId == currentUser);

            if (!string.IsNullOrEmpty(searchString)) docs = docs.Where(d => d.Title != null && d.Title.ToLower().Contains(searchString.ToLower())).ToList();
            if (!string.IsNullOrEmpty(searchCategory)) docs = docs.Where(d => d.Category == searchCategory).ToList();
            if (!string.IsNullOrEmpty(searchStatus)) docs = docs.Where(d => d.Status == searchStatus).ToList();

            docs = docs.OrderByDescending(d => d.CreatedAt).ToList();

            var auditLogs = new List<AuditLogModel>();
            // Только администратор видит журнал безопасности
            if (isAdmin)
            {
                QuerySnapshot auditSnap = await _db.Collection("AuditLogs").OrderByDescending("Timestamp").Limit(50).GetSnapshotAsync();
                foreach (var logSnap in auditSnap.Documents)
                {
                    var log = logSnap.ConvertTo<AuditLogModel>();
                    log.Id = logSnap.Id;
                    auditLogs.Add(log);
                }
            }
            ViewBag.AuditLogs = auditLogs;

            return View(docs);
        }

        [HttpPost]
        public async Task<IActionResult> AddDocument(DocumentModel model)
        {
            if (string.IsNullOrEmpty(model.Title) || model.Title.Length > 50 || !Regex.IsMatch(model.Title, @"[а-яА-ЯёЁ]"))
            {
                TempData["ErrorNotification"] = "Ошибка добавления: Название должно содержать русские буквы и быть не длиннее 50 символов.";
                return RedirectToAction("Index");
            }

            model.CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
            model.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
            model.OwnerId = User.Identity.Name;
            model.OwnerName = User.FindFirst("FullName")?.Value ?? User.Identity.Name;
            model.Version = 1;
            model.VersionHistory = new List<string>();
            if (string.IsNullOrEmpty(model.Status)) model.Status = "Черновик";

            if (model.UploadedFile != null)
            {
                string uploadsFolder = Path.Combine(_appEnvironment.WebRootPath, "uploads");
                if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);
                string uniqueFileName = Guid.NewGuid().ToString() + "_" + model.UploadedFile.FileName;
                using (var fileStream = new FileStream(Path.Combine(uploadsFolder, uniqueFileName), FileMode.Create))
                {
                    await model.UploadedFile.CopyToAsync(fileStream);
                }
                model.FileName = uniqueFileName;
            }

            CollectionReference collection = _db.Collection("Documents");
            await collection.AddAsync(model);

            await LogAction("Создание", $"Создан документ: '{model.Title}' (Категория: {model.Category})");
            TempData["SuccessNotification"] = $"Документ '{model.Title}' успешно добавлен.";

            return RedirectToAction("Index");
        }

        [HttpPost]
        public async Task<IActionResult> EditDocument(DocumentModel model)
        {
            if (string.IsNullOrEmpty(model.Title) || model.Title.Length > 30 || !Regex.IsMatch(model.Title, @"[а-яА-ЯёЁ]"))
            {
                TempData["ErrorNotification"] = "Ошибка изменения: Название должно содержать русские буквы и быть не длиннее 30 символов.";
                return RedirectToAction("Index");
            }

            DocumentReference docRef = _db.Collection("Documents").Document(model.Id);
            DocumentSnapshot oldDocSnap = await docRef.GetSnapshotAsync();
            if (!oldDocSnap.Exists) return RedirectToAction("Index");
            var oldDoc = oldDocSnap.ConvertTo<DocumentModel>();

            // Проверка: Редактировать может Администратор, Руководитель (Manager) или Владелец
            if (!User.IsInRole("Admin") && !User.IsInRole("Manager") && !User.IsInRole("Руководитель") && oldDoc.OwnerId != User.Identity.Name)
            {
                TempData["ErrorNotification"] = "У вас нет прав на редактирование этого документа.";
                return RedirectToAction("Index");
            }

            model.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
            model.CreatedAt = oldDoc.CreatedAt.Kind != DateTimeKind.Utc ? DateTime.SpecifyKind(oldDoc.CreatedAt, DateTimeKind.Utc) : oldDoc.CreatedAt;
            model.OwnerId = oldDoc.OwnerId;
            model.OwnerName = oldDoc.OwnerName;
            model.FileName = oldDoc.FileName;
            model.VersionHistory = oldDoc.VersionHistory ?? new List<string>();
            model.Version = oldDoc.Version == 0 ? 1 : oldDoc.Version;

            string actionDetails = $"Изменен документ '{model.Title}'.";

            if (model.UploadedFile != null)
            {
                if (!string.IsNullOrEmpty(oldDoc.FileName))
                {
                    model.VersionHistory.Add($"v{model.Version}|{oldDoc.FileName}|{DateTime.UtcNow:dd.MM.yyyy HH:mm}");
                }

                model.Version += 1;
                actionDetails += $" Обновлен файл (версия {model.Version}).";

                string uploadsFolder = Path.Combine(_appEnvironment.WebRootPath, "uploads");
                string uniqueFileName = Guid.NewGuid().ToString() + "_" + model.UploadedFile.FileName;
                using (var fileStream = new FileStream(Path.Combine(uploadsFolder, uniqueFileName), FileMode.Create))
                {
                    await model.UploadedFile.CopyToAsync(fileStream);
                }
                model.FileName = uniqueFileName;
            }

            if (model.Status != oldDoc.Status) actionDetails += $" Статус изменен на '{model.Status}'.";

            await docRef.SetAsync(model, SetOptions.Overwrite);
            await LogAction("Редактирование", actionDetails);

            TempData["SuccessNotification"] = $"Документ '{model.Title}' успешно обновлен.";
            return RedirectToAction("Index");
        }

        [HttpPost]
        public async Task<IActionResult> DeleteVersion(string id, string versionData)
        {
            if (!User.IsInRole("Admin"))
            {
                TempData["ErrorNotification"] = "Удаление версий доступно только администратору.";
                return RedirectToAction("Index");
            }

            DocumentReference docRef = _db.Collection("Documents").Document(id);
            DocumentSnapshot docSnap = await docRef.GetSnapshotAsync();

            if (docSnap.Exists)
            {
                var doc = docSnap.ConvertTo<DocumentModel>();
                if (doc.VersionHistory != null && doc.VersionHistory.Contains(versionData))
                {
                    doc.VersionHistory.Remove(versionData);

                    var parts = versionData.Split('|');
                    if (parts.Length > 1)
                    {
                        string fileName = parts[1];
                        string filePath = Path.Combine(_appEnvironment.WebRootPath, "uploads", fileName);
                        if (System.IO.File.Exists(filePath))
                        {
                            System.IO.File.Delete(filePath);
                        }
                    }

                    await docRef.SetAsync(doc, SetOptions.MergeAll);
                    await LogAction("Удаление версии", $"Удалена архивная версия файла из документа '{doc.Title}'");
                    TempData["SuccessNotification"] = "Версия файла успешно удалена.";
                }
            }
            return RedirectToAction("Index");
        }

        [HttpGet]
        public async Task<IActionResult> DeleteDocument(string id)
        {
            DocumentReference docRef = _db.Collection("Documents").Document(id);
            DocumentSnapshot docSnap = await docRef.GetSnapshotAsync();

            if (docSnap.Exists)
            {
                var doc = docSnap.ConvertTo<DocumentModel>();

                // Проверка: Удалять может только Админ или Владелец
                if (!User.IsInRole("Admin") && doc.OwnerId != User.Identity.Name)
                {
                    TempData["ErrorNotification"] = "У вас нет прав на удаление этого документа.";
                    return RedirectToAction("Index");
                }

                string uploadsFolder = Path.Combine(_appEnvironment.WebRootPath, "uploads");
                if (!string.IsNullOrEmpty(doc.FileName) && System.IO.File.Exists(Path.Combine(uploadsFolder, doc.FileName)))
                {
                    System.IO.File.Delete(Path.Combine(uploadsFolder, doc.FileName));
                }

                if (doc.VersionHistory != null)
                {
                    foreach (var hist in doc.VersionHistory)
                    {
                        var parts = hist.Split('|');
                        if (parts.Length > 1 && System.IO.File.Exists(Path.Combine(uploadsFolder, parts[1])))
                        {
                            System.IO.File.Delete(Path.Combine(uploadsFolder, parts[1]));
                        }
                    }
                }

                await docRef.DeleteAsync();
                await LogAction("Удаление", $"Удален документ: '{doc.Title}'");
                TempData["SuccessNotification"] = "Документ удален.";
            }
            return RedirectToAction("Index");
        }

    }
}