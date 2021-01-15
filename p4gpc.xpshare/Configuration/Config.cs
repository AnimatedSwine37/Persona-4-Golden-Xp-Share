using System.ComponentModel;
using p4gpc.xpshare.Configuration.Implementation;

namespace p4gpc.xpshare.Configuration
{
    public class Config : Configurable<Config>
    {
        /*
            User Properties:
                - Please put all of your configurable properties here.
                - Tip: Consider using the various available attributes https://stackoverflow.com/a/15051390/11106111
        
            By default, configuration saves as "Config.json" in mod folder.    
            Need more config files/classes? See Configuration.cs
        */

        [DisplayName("Verbose Mode")]
        [Description("Logs non-error information, mainly about how much xp was added and to whom.")]
        public bool verbose { get; set; } = false;

        [DisplayName("Shared XP Scale")]
        [Description("All shared xp that is added is multplied by this (1.0 gives the same amount of XP that the protagnoist gained)")]
        public float xpScale { get; set; } = 1.0F;

        /*[DisplayName("String")]
        [Description("This is a string.")]
        public string String { get; set; } = "Default Name";

        [DisplayName("Int")]
        [Description("This is an int.")]
        public int Integer { get; set; } = 42;

        [DisplayName("Bool")]
        [Description("This is a bool.")]
        public bool Boolean { get; set; } = true;

        [DisplayName("Float")]
        [Description("This is a floating point number.")]
        public float Float { get; set; } = 6.987654F;

        [DisplayName("Enum")]
        [Description("This is an enumerable.")]
        public SampleEnum Reloaded { get; set; } = SampleEnum.ILoveIt;

        public enum SampleEnum
        {
            NoOpinion,
            Sucks,
            IsMediocre,
            IsOk,
            IsCool,
            ILoveIt
        }
        */
    }
}
