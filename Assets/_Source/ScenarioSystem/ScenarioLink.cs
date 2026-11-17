using System;
using System.Text;
#if !UNITY_EDITOR
using UnityEngine;
using UnityEngine.Networking.PlayerConnection;
#endif

namespace ScenarioSystem
{
    /// <summary>Канал между редактором и запущенным билдом, тот же, по которому идут данные Profiler.</summary>
    public static class ScenarioLink
    {
        public static readonly Guid CommandId = new("8d3f4a62-2f6e-4b3a-9a51-5a0e7c1f1a01");
        public static readonly Guid ReplyId = new("8d3f4a62-2f6e-4b3a-9a51-5a0e7c1f1a02");

        public const string RunPrefix = "run:";
        public const string DonePrefix = "done:";
        public const string Quit = "quit";

        public static byte[] Encode(string text)
        {
            return Encoding.UTF8.GetBytes(text);
        }

        public static string Decode(byte[] data)
        {
            return Encoding.UTF8.GetString(data);
        }

#if !UNITY_EDITOR
        public static void ListenInPlayer(ScenarioRunner runner)
        {
            PlayerConnection.instance.Register(CommandId, message =>
            {
                var text = Decode(message.data);
                if (text == Quit) Application.Quit();
                else if (text.StartsWith(RunPrefix)) runner.Run(text.Substring(RunPrefix.Length));
            });

            runner.Finished += id => PlayerConnection.instance.Send(ReplyId, Encode(DonePrefix + id));
        }
#endif
    }
}
