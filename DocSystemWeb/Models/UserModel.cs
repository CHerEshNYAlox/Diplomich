using Google.Cloud.Firestore;

namespace DocSystemWeb.Models
{
    [FirestoreData]
    public class UserModel
    {
        [FirestoreDocumentId]
        public string Id { get; set; }

        [FirestoreProperty]
        public string Login { get; set; }

        [FirestoreProperty]
        public string Password { get; set; } // Хранит хэш пароля

        [FirestoreProperty]
        public string FullName { get; set; }

        [FirestoreProperty]
        public string Role { get; set; } // Admin, Manager, User

        [FirestoreProperty]
        public string Position { get; set; }
    }
}