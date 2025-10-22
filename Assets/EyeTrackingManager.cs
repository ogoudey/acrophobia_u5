using UnityEngine;
using System.IO;
using System.Runtime.InteropServices;
using ViveSR.anipal.Eye;
using System.Collections.Generic;

namespace ViveSR.anipal.Eye
{
    public class EyeTrackingManager : MonoBehaviour
    {
        public static EyeTrackingManager instance;
        private static EyeData eyeData = new EyeData();
        private static EyeData_v2 eyeDataV2 = new EyeData_v2();

        private bool eye_callback_registered = false;

        private static int callbackCount = 0;
        private const int logEveryN = 10;
        private static string logPath;
        private static StreamWriter writer;
        private static Queue<string> logQueue = new Queue<string>();
        internal class MonoPInvokeCallbackAttribute : System.Attribute
        {
            public MonoPInvokeCallbackAttribute() { }
        }

        void Start()
        {
            if (instance == null)
            {
                instance = this;
            }

            if (!SRanipal_Eye_Framework.Instance.EnableEye)
            {
                return;
            }

            logPath = Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop),
                "eye_tracking_log_test.csv"
            );

            writer = new StreamWriter(logPath);
            writer.WriteLine("Timestamp,PupilDiameterLeft,PupilDiameterRight"); // header row
            writer.Flush();

            SRanipal_Eye.WrapperRegisterEyeDataCallback(Marshal.GetFunctionPointerForDelegate((SRanipal_Eye.CallbackBasic)EyeCallback));
            Debug.Log("EyeCallback registered and logging to: " + logPath);
        }

        void OnApplicationQuit()
        {
            if (writer != null)
            {
                writer.Flush();
                writer.Close();
            }
        }

        void Update()
        {
            if (SRanipal_Eye_Framework.Status != SRanipal_Eye_Framework.FrameworkStatus.WORKING &&
                SRanipal_Eye_Framework.Status != SRanipal_Eye_Framework.FrameworkStatus.NOT_SUPPORT)
            {
                return;
            }

            if (SRanipal_Eye_Framework.Instance.EnableEyeDataCallback && !eye_callback_registered)
            {
                if (SRanipal_Eye_Framework.Instance.EnableEyeVersion == SRanipal_Eye_Framework.SupportedEyeVersion.version1)
                {
                    SRanipal_Eye.WrapperRegisterEyeDataCallback(Marshal.GetFunctionPointerForDelegate((SRanipal_Eye.CallbackBasic)EyeCallback));
                    Debug.Log("EyeTrackingManager: EyeCallback registered (v1)");
                }
                else
                {
                    SRanipal_Eye_v2.WrapperRegisterEyeDataCallback(Marshal.GetFunctionPointerForDelegate((SRanipal_Eye_v2.CallbackBasic)EyeCallbackV2));
                    Debug.Log("EyeTrackingManager: EyeCallbackV2 registered (v2)");
                }

                eye_callback_registered = true;
            }

            // Write any buffered data to file
            while (logQueue.Count > 0)
            {
                string line = logQueue.Dequeue();
                writer.WriteLine(line);
            }

            writer.Flush();
        }

        [MonoPInvokeCallback]
        private static void EyeCallback(ref EyeData eye_data)
        {
            if (callbackCount % logEveryN != 0) return;
            eyeData = eye_data; // need?

            var timestamp = eye_data.timestamp;
            var pupilLeft = eye_data.verbose_data.left.pupil_diameter_mm;
            var pupilRight = eye_data.verbose_data.right.pupil_diameter_mm;
            var gazeLeft = eye_data.verbose_data.left.gaze_direction_normalized;
            var gazeRight = eye_data.verbose_data.right.gaze_direction_normalized;


            // Format a CSV line
            string line = $"{timestamp:F3},{pupilLeft},{pupilRight}," +
                        $"{gazeLeft.x},{gazeLeft.y},{gazeLeft.z}," +
                        $"{gazeRight.x},{gazeRight.y},{gazeRight.z}";

            logQueue.Enqueue(line);

        }

        [MonoPInvokeCallback]
        private static void EyeCallbackV2(ref EyeData_v2 eye_data)
        {
            eyeDataV2 = eye_data;
            Debug.Log($"[EyeCallbackV2] Timestamp: {eyeDataV2.timestamp} | " +
                      $"PupilDiameter L: {eyeDataV2.verbose_data.left.pupil_diameter_mm}, R: {eyeDataV2.verbose_data.right.pupil_diameter_mm} | " +
                      $"GazeDirection L: {eyeDataV2.verbose_data.left.gaze_direction_normalized}, R: {eyeDataV2.verbose_data.right.gaze_direction_normalized}");
        }
    }
}
