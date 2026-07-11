using System.Windows.Input;

namespace RetroAudit.Models;

// Kullanıcı isteği: "ayrı ayrı badge olacak ben horror'u kaldırmak istediğimde tıklayıp
// kaldırabilecem" — stats bar'daki aktif filtre rozetlerinin (bkz. MainViewModel.
// RefreshActiveFilterChips) HER BİRİ tek bir değeri temsil eder (ör. Türler için "Shooter" ve
// "Horror" AYRI rozetler), tıklanınca SADECE o değeri kaldırır — bütün filtreyi değil.
public sealed record ActiveFilterChip(string Label, ICommand RemoveCommand);
