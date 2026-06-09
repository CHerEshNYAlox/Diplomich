using Google.Cloud.Firestore;
using System;

namespace DocSystemWeb.Models
{
    [FirestoreData]
    public class AuditLogModel
    {
        [FirestoreDocumentId]
        public string Id { get; set; }

        [FirestoreProperty]
        public string UserId { get; set; }

        [FirestoreProperty]
        public string UserName { get; set; }

        [FirestoreProperty]
        public string Action { get; set; }

        [FirestoreProperty]
        public string Details { get; set; }

        [FirestoreProperty]
        public DateTime Timestamp { get; set; }
    }
}