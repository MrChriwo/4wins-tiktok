using System;
using System.Collections.Generic;
using UnityEngine;

namespace FourWinsTikTok.Config
{
    [CreateAssetMenu(menuName = "4Wins TikTok/Config/Registration Gift Catalog", fileName = "RegistrationGiftCatalog")]
    public class RegistrationGiftCatalog : ScriptableObject
    {
        [SerializeField] private List<GiftItem> gifts = new List<GiftItem>();

        public IReadOnlyList<GiftItem> Gifts => gifts;

        [Serializable]
        public sealed class GiftItem
        {
            [SerializeField] private string name;
            [SerializeField] private Sprite icon;

            public string Name => name;
            public Sprite Icon => icon;
        }
    }
}