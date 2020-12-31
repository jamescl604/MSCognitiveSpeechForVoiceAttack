using System;
using System.Security.Cryptography;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Reflection;

namespace MSCognitiveTextToSpeech
{
    public static class Utils
    {
       
        public static string GetPluginVersion()
        {
            return Assembly.GetExecutingAssembly().GetName().Version.ToString();
        }

        public static T PickRandom<T>(this List<T> enumerable)
        {
            int index = new Random().Next(0, enumerable.Count);
            return enumerable[index];
        }
       

        public static string GetHashedName(MemoryStream ms)
        {

            if (ms == null || ms.Length == 0) return null;

            var sha = new SHA1CryptoServiceProvider();
            byte[] hashedBytes = sha.ComputeHash(ms.ToArray());

            var name = new System.Text.StringBuilder();
            foreach (Byte hashed in hashedBytes)
                //converts byte to base16 representation(0 - F) so it's filename safe
                name.AppendFormat("{0:x2}", hashed);

            return name.ToString();

        }

        //returns a unique filename based on the hash of a string
        public static string GetHashedName(string input)
        {

            if (String.IsNullOrWhiteSpace(input)) return null;

            var sha = new SHA1CryptoServiceProvider();
            byte[] hashedBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));

            var name = new System.Text.StringBuilder();
            foreach (Byte hashed in hashedBytes)
                //converts byte to base16 representation(0 - F) so it's filename safe
                name.AppendFormat("{0:x2}", hashed);

            return name.ToString();
        }
    }
}
