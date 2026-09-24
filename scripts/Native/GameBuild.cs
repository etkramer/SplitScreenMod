namespace Etkramer.SplitScreen.Native;

public static class GameBuild
{
    public static bool IsSteam => GameDefine.Current.Name == "Steam";


    public static IntPtr Offset(IntPtr epic, IntPtr steam) =>
        GameDefine.Current switch
        {
            GameDefineEpic => epic,
            GameDefineSteam => steam,
            var name => throw new NotSupportedException(
                $"SplitScreen mod: unsupported build '{name}'"
            ),
        };
}
