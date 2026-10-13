using System;

namespace PaintMaze.Services
{
    public enum CosmeticPurchaseResult
    {
        Purchased,
        AlreadyOwned,
        InsufficientCoins,
        UnknownSkin
    }

    /// <summary>
    /// Validates cosmetic purchases against the catalog and persists ownership,
    /// equipment, and the resulting coin balance.
    /// </summary>
    public sealed class CosmeticService
    {
        public event Action Changed;

        private readonly EconomySyncService _economySync;

        public CosmeticService(EconomySyncService economySync = null)
        {
            _economySync = economySync;
        }

        public CosmeticPurchaseResult TryBuy(string skinId)
        {
            if (!CosmeticCatalog.TryGet(skinId, out BallSkinDefinition skin))
                return CosmeticPurchaseResult.UnknownSkin;
            if (SaveService.OwnsSkin(skin.Id))
                return CosmeticPurchaseResult.AlreadyOwned;
            if (!SaveService.TrySpendCoins(skin.Price))
                return CosmeticPurchaseResult.InsufficientCoins;

            SaveService.GrantSkin(skin.Id);
            SaveService.EquippedSkinId = skin.Id;
            PersistChange();
            return CosmeticPurchaseResult.Purchased;
        }

        public bool TryEquip(string skinId)
        {
            if (!CosmeticCatalog.TryGet(skinId, out BallSkinDefinition skin))
                return false;
            if (!SaveService.OwnsSkin(skin.Id))
                return false;
            if (SaveService.EquippedSkinId == skin.Id)
                return true;

            SaveService.EquippedSkinId = skin.Id;
            PersistChange();
            return true;
        }

        private void PersistChange()
        {
            _economySync?.PushLocal();
            Changed?.Invoke();
        }
    }
}
