using UnityEngine;

namespace FourWinsTikTok.World
{
    public class ConnectFourColumnClickTarget : MonoBehaviour
    {
        [field: SerializeField] public int ColumnIndex { get; private set; }

        public void SetColumnIndex(int columnIndex)
        {
            ColumnIndex = columnIndex;
        }
    }
}
