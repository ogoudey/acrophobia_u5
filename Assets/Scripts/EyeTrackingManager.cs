using UnityEngine;
using System;
using System.IO;
using System.Runtime.InteropServices;
using ViveSR.anipal.Eye;
using System.Collections.Generic;

namespace ViveSR.anipal.Eye
{
    public class EyeTrackingManager : MonoBehaviour
    {
        public static EyeTrackingManager instance;

        [SerializeField]
        private string subjectName = "Default Dave";
        private static EyeData eyeData = new EyeData();
        private static EyeData_v2 eyeDataV2 = new EyeData_v2();

        private bool eye_callback_registered = false; // This should have a better interface

        private static int callbackCount = 0;
        private const int logEveryN = 10;
        private static string logPath;
        private static StreamWriter writer;
        private static Queue<string> logQueue = new Queue<string>();

        // Luminance stuff
        private bool luminanceEnabled = true;
        private int luminanceWidth = 256;
        private int luminanceHeight = 144;
        private Camera luminanceCamera;
        private Texture2D luminanceTexture;
        private RenderTexture luminenceRenderTexture;
        public static float luminance = -1.0f;
        private float luminanceTime = -1.0f;
        private float luminanceRate = 1.0f / 10.0f;


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

            string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");  // e.g., 2025-10-22_14-30-00
            string logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "AcroGenData",
                subjectName,
                sceneName,
                timestamp
            );
            Directory.CreateDirectory(logDirectory);
            string logPath = Path.Combine(logDirectory, "eye_tracking_log_test.csv");

            writer = new StreamWriter(logPath);
            writer.WriteLine("Timestamp,PupilDiameterLeft,PupilDiameterRight,GazeLeftX,GazeLeftY,GazeLeftZ,GazeRightX,GazeRightY,GazeRightZ"); // header row
            // Add CalculatedFear header
            writer.Flush();

            if (luminanceEnabled)
            {
                CreateLuminanceCamera();
            }


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

        private void FixedUpdate()
        {
            if (luminanceEnabled && luminanceCreated && Time.time - luminanceTime >= luminanceRate)
            {
                EyeTrackingManager.luminance = GetLuminance(luminanceCamera.targetTexture);
                //Debug.LogFormat("ETM Luminance {0}", EyeTrackingManager.luminance);
                luminanceTime = Time.time;
            }
        }

        private float GetLuminance(RenderTexture renderTexture)
        {
            RenderTexture oldRenderTexture = RenderTexture.active;
            RenderTexture.active = renderTexture;

            Texture2D texture2D = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGBAFloat, false);
            texture2D.ReadPixels(new Rect(0, 0, texture2D.width, texture2D.height), 0, 0, false);
            texture2D.Apply();

            Color[] allColors = texture2D.GetPixels();

            float totalLuminance = 0f;

            foreach (Color color in allColors)
            {
                totalLuminance += (color.r * 0.2126f) + (color.g * 0.7152f) + (color.b * 0.0722f);
            }

            float averageLuminance = totalLuminance / allColors.Length;


            RenderTexture.active = oldRenderTexture;
            UnityEngine.Object.Destroy(texture2D);

            return averageLuminance;
        }

        private void CreateLuminanceCamera()
        {
            GameObject cameraChild = new GameObject();
            cameraChild.name = "Luminance Camera";
            cameraChild.transform.parent = cam.gameObject.transform;
            luminanceCamera = cameraChild.AddComponent(typeof(Camera)) as Camera;
            luminanceCamera.fieldOfView = cam.fieldOfView;
            luminanceCamera.targetTexture = new RenderTexture(luminanceWidth, luminanceHeight, 24);
            cameraChild.transform.localPosition = new Vector3(0, 0, 0);
            cameraChild.transform.localRotation = new Quaternion(0, 0, 0, 0);
            luminanceCreated = true;
        }
    


    }
}
