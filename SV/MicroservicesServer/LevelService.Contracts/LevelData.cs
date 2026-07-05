using MessagePack;

namespace LevelService.Contracts
{
    [MessagePackObject]
    public class LevelData
    {
        [Key(0)]
        public int currentLevel;
    }
}
