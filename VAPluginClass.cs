//===================================================================================================================================================
// PROJECT: Microsoft Cognitive Text-to-Speech plugin for Voice Attack
// PURPOSE: This class adds Cognitive Text-to-Speech support for Voice Attack profiles (https://voiceattack.com/)
// AUTHOR: James Clark
// Licensed under the MS-PL license. See LICENSE.md file in the project root for full license information.
// Note: this project is not endorsed or anyway supported by Microsoft Corporation.  It simply uses Microsoft services to achieve it's functionality.
//===================================================================================================================================================

using System;
using System.Xml;
using System.Xml.Linq;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;
using Microsoft.CognitiveServices.Speech;
using NAudio;
using NAudio.Wave;
using NAudio.Dmo.Effect;


namespace MSCognitiveTextToSpeech
{
    public class VoiceAttackPlugin
    {
        const string VARIABLE_NAMESPACE = "MSCognitiveTextToSpeech";
        const string LOG_PREFIX = "MSCognitiveTextToSpeech: ";
        const string LOG_NORMAL = "purple";
        const string LOG_ERROR = "red";
        const string LOG_INFO = "grey";                        

        /// <summary>
        /// Name of the plug-in as it should be shown in the UX
        /// </summary>
        public static string VA_DisplayName()
        {
            return "MSCognitiveTextToSpeech - v1.0";  
        }

        /// <summary>
        /// Extra information to display about the plug-in
        /// </summary>
        /// <returns></returns>
        public static string VA_DisplayInfo()
        {
            return "Adds the ability to use Microsoft Cognitive Text-to-Speech services with Voice Attack profiles";  
        }

        /// <summary>
        /// Uniquely identifies the plugin
        /// </summary>
        public static Guid VA_Id()
        {            
            return new Guid("{B2686B52-77A4-4036-A1E5-2F5E3680A4B7}");  
        }
        
        /// <summary>
        /// Used to stop any long running processes inside the plugin
        /// </summary>
        public static void VA_StopCommand()  
        {
            // plugin has no long running processes
        }
        
        /// <summary>
        /// Runs when Voice Attack loads and processes plugins (runs once when the app launches)
        /// </summary>
        public static void VA_Init1(dynamic vaProxy)
        {

            // uncomment this line to force the debugger to attach at the very start of the class being created
            //System.Diagnostics.Debugger.Launch();

            // look for old sound files we can delete
            PruneCacheDirectory(vaProxy);

            //if (!SupportedProfile(vaProxy)) return;

        }

        /// <summary>
        /// Handles clean up before Voice Attack closes
        /// </summary>
        public static void VA_Exit1(dynamic vaProxy)
        {
            // no clean up needed
        }

        /// <summary>
        /// Main function used to process commands from Voice Attack
        /// </summary>
        public static async Task VA_Invoke1(dynamic vaProxy)
        {
            string context;

            // see if we should run for this profile
            //if (!SupportedProfile(vaProxy)) return;

            // abort if we have nothing to synthesize
            if (String.IsNullOrEmpty(vaProxy.Context))
            {
                vaProxy.WriteToLog(LOG_PREFIX + "No 'Context' value was provided. Aborting text-to-speech.", LOG_ERROR);
                return;
            }
            else
                context = vaProxy.Context;

            // since the context could contain dynamic tokens/phrases, we need to extract one
            string[] possiblePhrases = vaProxy.Utility.ExtractPhrases(context);
            string selectedPhrase = possiblePhrases.ToList().PickRandom();
   

            // see if we have a valid configuration file
            Configuration config = new Configuration();
            if (!config.Exists() && config.SettingCount == 0)
            {
                vaProxy.WriteToLog(LOG_PREFIX + "Config file is missing or invalid. Location: " + config.Path, LOG_ERROR);
                return;
            }

            // proceed with the text-to-speech process
            if (DebugMode(vaProxy)) vaProxy.WriteToLog(LOG_PREFIX + "Processing context: " + context, LOG_INFO);
            await InvokeTextToSpeech(vaProxy, selectedPhrase);
            
            // if we get this far we've processed the command
            vaProxy.WriteToLog(LOG_PREFIX + "Context processed: " + context, LOG_NORMAL);

        }

        /// <summary>
        /// Handles generating the speech audio
        /// </summary>
        public static async Task InvokeTextToSpeech(dynamic vaProxy, string message)
        {
            WaveFileReader speechResultWavReader = null;            
            string cacheDirectory = GetCacheDirectory(vaProxy);
            bool cacheEnabled = GetCacheEnabled(vaProxy);

            // The full list of supported voices can be found here:
            // https://docs.microsoft.com/azure/cognitive-services/speech-service/language-support
            // favs: en-IE-EmilyNeural, en-US-JennyNeural*, en-AU-WilliamNeural, en-US-AriaNeural*            
            var config = SpeechConfig.FromSubscription(GetAzureSubscriptionKey(vaProxy), GetAzureRegion(vaProxy));
            config.SpeechSynthesisVoiceName = GetVoiceName(vaProxy);
            config.SpeechSynthesisLanguage = GetVoiceLanguage(vaProxy);
            config.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Riff24Khz16BitMonoPcm);


            if (DebugMode(vaProxy))
            {
                vaProxy.WriteToLog(LOG_PREFIX + "Voice Name: " + config.SpeechSynthesisVoiceName, LOG_INFO);
                vaProxy.WriteToLog(LOG_PREFIX + "Language: " + config.SpeechSynthesisLanguage, LOG_INFO);                
                vaProxy.WriteToLog(LOG_PREFIX + "Region: " + GetAzureRegion(vaProxy), LOG_INFO);
                vaProxy.WriteToLog(LOG_PREFIX + "CacheEnabled: " + cacheEnabled.ToString(), LOG_INFO);
                vaProxy.WriteToLog(LOG_PREFIX + "CacheDirectory: " + cacheDirectory, LOG_INFO);
                vaProxy.WriteToLog(LOG_PREFIX + "ApplyRadioEffect: " + GetAddRadioEffect(vaProxy).ToString(), LOG_INFO);
            }


            //string msg = "This is a test message";
            string msg = message;

            XElement ssml = GenerateSSML(config, msg, vaProxy);
            string ssmlText = ssml.ToString();
            Debug.WriteLine("SSML: " + ssml.ToString()); 
            

            // generate a unique filename based on the ssxml mesage so it can be cached/saved to disk.  
            string wavFilePath = System.IO.Path.Combine(cacheDirectory, Utils.GetHashedName(ssmlText) + ".wav");

            // if the wav file already exists on disk, use it, else call out to the Speech service to generate the audio
            if (File.Exists(wavFilePath))
            {
                try
                {
                    speechResultWavReader = new WaveFileReader(wavFilePath);
                    vaProxy.WriteToLog(LOG_PREFIX + "Getting audio from cache: " + wavFilePath, LOG_INFO);
                }
                catch (Exception ex)
                {
                    vaProxy.WriteToLog(LOG_PREFIX + "Failed to read cached/saved audio file " + wavFilePath + ". Msg: " + ex.Message, LOG_ERROR);                    
                }
            }

            if (speechResultWavReader == null)
            {

                vaProxy.WriteToLog(LOG_PREFIX + "Generating audio from online speech service.", LOG_INFO);

                // Go synthesize the text.  Note: null needs to be passed for the audio config          
                using var synthesizer = new SpeechSynthesizer(config, null);
                using var result = await synthesizer.SpeakSsmlAsync(ssmlText);
                if (result.Reason == ResultReason.SynthesizingAudioCompleted)
                {
                    // cache/save the audio to a wav file if caching is enabled
                    if (cacheEnabled)
                    {
                        try
                        {
                            using var speechAudioStream = AudioDataStream.FromResult(result);
                            await speechAudioStream.SaveToWaveFileAsync(wavFilePath);
                        }
                        catch (Exception ex)
                        {
                            vaProxy.WriteToLog(LOG_PREFIX + "Audio file could not be cached/saved to disk. Msg: " + ex.Message, LOG_ERROR);                            
                        }
                    }

                    speechResultWavReader = new WaveFileReader(new MemoryStream(result.AudioData));

                }
                else if (result.Reason == ResultReason.Canceled)
                {
                    var cancellation = SpeechSynthesisCancellationDetails.FromResult(result);
                    
                    if (cancellation.Reason == CancellationReason.Error)
                        vaProxy.WriteToLog(LOG_PREFIX + "SpeechSynthesis failed. ErrorCode: " + cancellation.ErrorCode + " ErrorDetails: " + cancellation.ErrorDetails, LOG_ERROR);
                    else
                        vaProxy.WriteToLog(LOG_PREFIX + "SpeechSynthesis cancelled. Reason: " + cancellation.Reason, LOG_ERROR);

                    return;
                }
            }

            if (speechResultWavReader == null || speechResultWavReader.Length == 0)
            {
                vaProxy.WriteToLog(LOG_PREFIX + "No audio data was able to be obtained.", LOG_ERROR);
                return;
            }

            // the synthesized audio seems to have added dead silence at the end so we'll trim it off
            speechResultWavReader = TrimFromEnd(speechResultWavReader, TimeSpan.FromSeconds(.4));

            // add radio effect if desired, then play the audio
            if (GetAddRadioEffect(vaProxy))
                playAudio(AddRadioEffect(speechResultWavReader, vaProxy), true);
            else
                playAudio(speechResultWavReader.ToSampleProvider());

            speechResultWavReader.Close();

            if (DebugMode(vaProxy)) vaProxy.WriteToLog(LOG_PREFIX + "Speech synthesizing complete.", LOG_INFO);            
        }

        /// <summary>
        /// Checks to see if the agent should run for the current Voice Attack profile or otherwise sleep
        /// </summary>
        private static bool SupportedProfile(dynamic vaProxy)
        {
            if (!vaProxy.Command.Exists("MSCognitiveTextToSpeech"))
            {
                if (DebugMode(vaProxy)) vaProxy.WriteToLog(LOG_PREFIX + "'MSCognitiveTextToSpeech' command not found in this profile.  Standing down.", LOG_INFO);
                return false;
            }
            else
            {
                if (DebugMode(vaProxy)) vaProxy.WriteToLog(LOG_PREFIX + "Profile is a match.  Plugin enabled.", LOG_INFO);
                return true;
            }

        }
        
        /// <summary>
         /// enables more detailed logging in the VA message window         
         /// </summary>
        private static bool DebugMode(dynamic vaProxy)
        {
            bool? result = vaProxy.GetBoolean(VARIABLE_NAMESPACE + ".DebugMode");
            return result.HasValue ? (bool)result : new Configuration().Setting<bool>("DebugMode");
        }

        private static string GetVoiceName(dynamic vaProxy)
        {
            string result = vaProxy.GetText(VARIABLE_NAMESPACE + ".DefaultVoiceName");

            return !String.IsNullOrWhiteSpace(result) ? result : new Configuration().Setting<string>("DefaultVoiceName");
        }
        private static string GetVoiceLanguage(dynamic vaProxy)
        {
            string result = vaProxy.GetText(VARIABLE_NAMESPACE + ".DefaultVoiceLanguage");

            return !String.IsNullOrWhiteSpace(result) ? result : new Configuration().Setting<string>("DefaultVoiceLanguage");
        }
        private static string GetCacheDirectory(dynamic vaProxy)
        {
            string dir = new Configuration().Setting<string>("CacheDirectory");

            dir = !String.IsNullOrWhiteSpace(dir) ? dir : Path.Combine(vaProxy.SessionState["VA_SOUNDS"], VARIABLE_NAMESPACE);

            try
            {
                // this will create the directory if it doesn't already exist
                Directory.CreateDirectory(dir);
            }
            catch (UnauthorizedAccessException ex)
            {
                vaProxy.WriteToLog(LOG_PREFIX + "Permission was denied when creating the cache directory.  Msg: " + ex.Message, LOG_ERROR);
            }
            catch (Exception ex)
            {
                vaProxy.WriteToLog(LOG_PREFIX + "Failed to create the cache directory. Msg: " + ex.Message, LOG_ERROR);
            }

            return dir;
        }
        private static bool GetCacheEnabled(dynamic vaProxy)
        {
            bool? result = vaProxy.GetBoolean(VARIABLE_NAMESPACE + ".CacheEnabled");
            return result.HasValue ? (bool)result : new Configuration().Setting<bool>("CacheEnabled");
        }
        private static int GetCacheDurationInDays(dynamic vaProxy)
        {
            int? result = vaProxy.GetBoolean(VARIABLE_NAMESPACE + ".CacheDurationInDays");
            return result.HasValue ? (int)result : new Configuration().Setting<int>("CacheDurationInDays");
        }
        private static bool GetAddRadioEffect(dynamic vaProxy)
        {            
            bool? result = vaProxy.GetBoolean(VARIABLE_NAMESPACE + ".AddRadioEffect");            
            return result.HasValue ? (bool)result : new Configuration().Setting<bool>("AddRadioEffect");
        }
        private static string GetAzureSubscriptionKey(dynamic vaProxy)
        {
            string result = vaProxy.GetBoolean(VARIABLE_NAMESPACE + ".AzureSubscriptionKey");
            return !String.IsNullOrWhiteSpace(result) ? result : new Configuration().Setting<string>("AzureSubscriptionKey");
        }
        private static string GetAzureRegion(dynamic vaProxy)
        {
            string result = vaProxy.GetBoolean(VARIABLE_NAMESPACE + ".AzureRegion");
            return !String.IsNullOrWhiteSpace(result) ? result : new Configuration().Setting<string>("AzureRegion");
        }
      
        /// <summary>
        /// Handles playing the speech audio to the default audio device
        /// </summary>
        public static void playAudio(ISampleProvider audio, bool waitUntilFinished = true)
        {
            using var player = new WaveOutEvent();
            player.Init(audio);
            player.Play();


            if (waitUntilFinished)
            {
                while (player.PlaybackState != PlaybackState.Stopped)
                {
                    System.Threading.Thread.Sleep(100);
                }
            }
        }

        /// <summary>
        /// Creates a properly formed SSML xml message to send to the speech service
        /// </summary>
        public static XElement GenerateSSML(SpeechConfig config, string message, dynamic vaProxy)
        {
            /*  Example of what the ssxml is supposed to look like (see https://azure.microsoft.com/en-us/services/cognitive-services/text-to-speech/)

               <speak version="1.0" xmlns="http://www.w3.org/2001/10/synthesis" xmlns:mstts="https://www.w3.org/2001/mstts" xml:lang="en-US">
                   <voice name="en-US-AriaNeural">
                       <mstts:express-as style="cheerful">That'd be just amazing!</mstts:express-as>
                   </voice>
               </speak>
           */

            // need to wrap the incoming message with paragraph tags so a message without ssml tags can still be used
            message = "<p>" + message + "</p>";

            // define the namespaces here then we'll reference them in a couple places
            XNamespace syn = "http://www.w3.org/2001/10/synthesis";
            XNamespace mstts = "https://www.w3.org/2001/mstts";
            XNamespace emo = "http://www.w3.org/2009/10/emotionml";

            // we need to setup a namespace manager and name table just so we can parse the provided text which may contain
            // xml content with namespace values.  If we don't do this setup, the parse will fail on any unknown namespaces in the content.
            NameTable nameTable = new NameTable();
            XmlNamespaceManager nameSpaceManager = new XmlNamespaceManager(nameTable);
            nameSpaceManager.AddNamespace("syn", syn.NamespaceName);
            nameSpaceManager.AddNamespace("mstts", mstts.NamespaceName);
            nameSpaceManager.AddNamespace("emo", emo.NamespaceName);

            // parse the content provided into xml, this also ensures it's well-formed
            try
            {
                XmlParserContext parserContext = new XmlParserContext(null, nameSpaceManager, null, XmlSpace.None);
                using var reader = new XmlTextReader(message, XmlNodeType.Element, parserContext);
                XElement parsedMessage = XElement.Load(reader);

                // Setup the main SSML xml document then insert the parsed message.  Note: the elements are case sensitive.       
                XElement ssml = new XElement(syn + "speak",
                    new XAttribute("xmlns", syn.NamespaceName),
                    new XAttribute(XNamespace.Xmlns + "mstts", mstts.NamespaceName),
                    new XAttribute(XNamespace.Xmlns + "emo", emo.NamespaceName),
                    new XAttribute(XNamespace.Xml + "version", "1.0"),
                    new XAttribute(XNamespace.Xml + "lang", "en-US"),  // Microsoft examples seem to always keep this en-US even when using different languages 
                    new XElement(XNamespace.None + "voice",
                        new XAttribute(XNamespace.None + "name", config.SpeechSynthesisVoiceName),
                        parsedMessage)
                );

                return ssml;
            }
            catch (Exception ex)
            {
                vaProxy.WriteToLog(LOG_PREFIX + "Speech content could not be parsed. Check that it is correctly formed. Msg: " + ex.Message, LOG_ERROR);                
                return null;
            }

        }

        /// <summary>
        ///  Reduces the duration of the audio to the time span given.  Only designed to work with wav files.
        /// </summary>
        public static WaveFileReader SetDuration(WaveFileReader source, TimeSpan duration)
        {

            var outputStream = new MemoryStream();
            using (var writer = new WaveFileWriter(new NAudio.Utils.IgnoreDisposeStream(outputStream), source.WaveFormat))
            {

                float bytesPerMillisecond = source.WaveFormat.AverageBytesPerSecond / 1000f;

                int startPos = 0;
                startPos = startPos - startPos % source.WaveFormat.BlockAlign;

                int endBytes = (int)Math.Round(duration.TotalMilliseconds * bytesPerMillisecond);
                endBytes = endBytes - endBytes % source.WaveFormat.BlockAlign;
                int endPos = endBytes;


                source.Position = 0;
                byte[] buffer = new byte[source.BlockAlign * 1024];
                while (source.Position < endPos)
                {
                    int bytesRequired = (int)(endPos - source.Position);
                    if (bytesRequired > 0)
                    {
                        int bytesToRead = Math.Min(bytesRequired, buffer.Length);
                        int bytesRead = source.Read(buffer, 0, bytesToRead);
                        if (bytesRead > 0)
                        {
                            writer.Write(buffer, 0, bytesRead);
                        }
                    }
                }
            }
            outputStream.Position = 0;

            return new WaveFileReader(outputStream);
        }

        /// <summary>
        /// Trims the duration given from the end of the audio file.  Designed to work with wav files only.
        /// </summary>
        public static WaveFileReader TrimFromEnd(WaveFileReader source, TimeSpan duration)
        {

            var outputStream = new MemoryStream();
            using (var writer = new WaveFileWriter(new NAudio.Utils.IgnoreDisposeStream(outputStream), source.WaveFormat))
            {

                float bytesPerMillisecond = source.WaveFormat.AverageBytesPerSecond / 1000f;

                int startPos = 0;
                startPos = startPos - startPos % source.WaveFormat.BlockAlign;

                long bytesToTrimOff = (int)Math.Round(duration.TotalMilliseconds * bytesPerMillisecond);
                long endBytes = source.Length - bytesToTrimOff;
                endBytes = endBytes - endBytes % source.WaveFormat.BlockAlign;
                long endPos = endBytes;


                source.Position = 0;
                byte[] buffer = new byte[source.BlockAlign * 1024];
                while (source.Position < endPos)
                {
                    int bytesRequired = (int)(endPos - source.Position);
                    if (bytesRequired > 0)
                    {
                        int bytesToRead = Math.Min(bytesRequired, buffer.Length);
                        int bytesRead = source.Read(buffer, 0, bytesToRead);
                        if (bytesRead > 0)
                        {
                            writer.Write(buffer, 0, bytesRead);
                        }
                    }
                }
            }
            outputStream.Position = 0;

            return new WaveFileReader(outputStream);
        }

        /// <summary>
        ///  Makes the audio sound like it's coming through a radio
        /// </summary>
        public static ISampleProvider AddRadioEffect(WaveFileReader source, dynamic vaProxy)
        {

            // Gets the pre-and-post radio click sound.  Resamples it (if needed) to ensure it's the same sample rate as the main audio            
            var reader = new WaveFileReader(Properties.Resources.radio_click);
            var conversionStream = new MediaFoundationResampler(reader, source.WaveFormat);
            ISampleProvider radioClickSound = conversionStream.ToSampleProvider();

            var reader2 = new WaveFileReader(Properties.Resources.radio_click);
            var conversionStream2 = new MediaFoundationResampler(reader2, source.WaveFormat);
            ISampleProvider radioClickSound2 = conversionStream2.ToSampleProvider();


            var distortedStream = new DmoEffectWaveProvider<DmoDistortion, DmoDistortion.Params>(source);
            var distortionEffect = distortedStream.EffectParams;
            distortionEffect.Edge = 10;
            //distortionEffect.Gain = -6;

            //var volumeSampleProvider = new NAudio.Wave.SampleProviders.VolumeSampleProvider(distortedStream.ToSampleProvider());
            //volumeSampleProvider.Volume = 0.1f;

            var bands = new EqualizerBand[]
            {
                    new EqualizerBand {Bandwidth =0.4f, Frequency = 100, Gain = -20},
                    new EqualizerBand {Bandwidth =0.4f, Frequency = 200, Gain = -10},
                    new EqualizerBand {Bandwidth =0.4f, Frequency = 400, Gain = -10},
                    new EqualizerBand {Bandwidth =0.4f, Frequency = 800, Gain = 10},
                    new EqualizerBand {Bandwidth =0.4f, Frequency = 1200, Gain = 10},
                    new EqualizerBand {Bandwidth =0.4f, Frequency = 2400, Gain = 10},
                    new EqualizerBand {Bandwidth =0.4f, Frequency = 4800, Gain = -25},
                    new EqualizerBand {Bandwidth =0.4f, Frequency = 9600, Gain = -30},
            };
            var equalizedStream = new Equalizer(distortedStream.ToSampleProvider(), bands);


            /*
            var compressedStream = new DmoEffectWaveProvider<DmoCompressor, DmoCompressor.Params>(equalizedStream.ToWaveProvider());
            var compressionEffect = compressedStream.EffectParams;
            compressionEffect.Ratio = 2.0f;
            compressionEffect.Attack = 0.8f;
            compressionEffect.Release = 0.8f;
            */

            try
            {
                // note: for this chaining to work, all the samples need to be using the same sample rate
                return radioClickSound.FollowedBy(equalizedStream).FollowedBy(radioClickSound2);
            }
            catch (Exception ex)
            {
                vaProxy.WriteToLog(LOG_PREFIX + "Radio clicks and Audio could not be chained. They may be using different sample or bit rates. Msg: " + ex.Message, LOG_ERROR);
                return equalizedStream;
            }            

        }

        /// <summary>
        /// Removes old/expired wav files from the cache directory
        /// </summary>        
        public static void PruneCacheDirectory(dynamic vaProxy)
        {
            var cacheDirectory = GetCacheDirectory(vaProxy);
            var cacheExpirationInDays = GetCacheDurationInDays(vaProxy);
            var numFilesRemoved = 0;

            try
            {
                var files = new DirectoryInfo(cacheDirectory).GetFiles("*.wav");
                foreach (var file in files)
                {
                    // age is based on when the file was last accessed (not when it was created)
                    if (DateTime.UtcNow - file.LastAccessTimeUtc > TimeSpan.FromDays(cacheExpirationInDays))
                    {
                        File.Delete(file.FullName);
                        numFilesRemoved++;
                    }
                }
            }
            catch (Exception ex)
            {
                vaProxy.WriteToLog(LOG_PREFIX + "Error trying to delete old files from the cache directory. Msg: " + ex.Message, LOG_ERROR);
                return;
            }
            
            vaProxy.WriteToLog(LOG_PREFIX + "Removed " + numFilesRemoved.ToString() + " files from the cache.", LOG_INFO);

        }
    }
}




