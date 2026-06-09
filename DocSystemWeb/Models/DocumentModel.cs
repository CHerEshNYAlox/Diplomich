using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;

namespace DocSystemWeb.Models
{
    [FirestoreData]
    public class DocumentModel
    {
        [FirestoreDocumentId]
        public string Id { get; set; }

        [FirestoreProperty]
        public string Title { get; set; }

        [FirestoreProperty]
        public string Category { get; set; }

        public IFormFile UploadedFile { get; set; }

        [FirestoreProperty]
        public string FileName { get; set; }

        [FirestoreProperty]
        public DateTime CreatedAt { get; set; }

        [FirestoreProperty]
        public DateTime UpdatedAt { get; set; }

        [FirestoreProperty]
        public string OwnerId { get; set; }

        [FirestoreProperty]
        public string OwnerName { get; set; }

        [FirestoreProperty]
        public string Status { get; set; }

        [FirestoreProperty]
        public int Version { get; set; } = 1;

        [FirestoreProperty]
        public List<string> VersionHistory { get; set; } = new List<string>();

        [FirestoreProperty]
        public string Description { get; set; }

        // Новое поле для выдачи прав конкретным пользователям (логины через запятую)
        [FirestoreProperty]
        public string SharedWithLogins { get; set; }
    }
}