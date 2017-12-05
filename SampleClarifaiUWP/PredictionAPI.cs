using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Clarifai.API;
using Clarifai.DTOs;
using Clarifai.DTOs.Inputs;
using Clarifai.DTOs.Predictions;

namespace SampleClarifaiUWP
{
    /// <summary>
    /// This class interacts with the Clarifai API.
    /// </summary>
    public class PredictionAPI
    {
        /// <summary>
        /// The Clarifai client. All API methods are implemented as methods of this interface.
        /// </summary>
        private readonly IClarifaiClient _clarifaiClient;

        /// <summary>
        /// A collection of public concept models and their IDs.
        /// You could add your own custom concept model IDs here.
        /// </summary>
        private readonly Dictionary<string, string> _models = new Dictionary<string, string>
        {
            {"ApparelModel", "e0be3b9d6a454f0493ac3a30784001ff"},
            {"FoodModel", "bd367be194cf45149e75f01d59f77ba7"},
            {"GeneralModel", "aaa03c23b3724a16a56b629203edc62c"},
            {"LandscapeQualityModel", "bec14810deb94c40a05f1f0eb3c91403"},
            {"ModerationModel", "d16f390eb32cad478c7ae150069bd2c6"},
            {"NsfwModel", "e9576d86d2004ed1a38ba0cf39ecb4b1"},
            {"PortraitQualityModel", "de9bd05cfdbf4534af151beb2a5d0953"},
            {"TexturesAndPatternsModel", "fbefb47f9fdb410e8ce14f24f54b47ff"},
            {"TravelModel", "eee28c313d69466f836ab83287a54ed9"},
            {"WeddingModel", "c386b7a870114f4a87477c0824499348"},
        };

        /// <summary>
        /// Ctor.
        /// </summary>
        /// <param name="clarifaiApiKey">the Clarifai API key</param>
        public PredictionAPI(string clarifaiApiKey)
        {
            _clarifaiClient = new ClarifaiClient(clarifaiApiKey);
        }

        /// <summary>
        /// Takes an array of bytes that's a camera output and predicts concepts located on it
        /// using a selected model.
        /// </summary>
        /// <param name="imageBytes">the image bytes</param>
        /// <param name="selectedModel">the selected model</param>
        /// <returns></returns>
        public async Task<string> PredictConcepts(byte[] imageBytes, string selectedModel)
        {
            var image = new ClarifaiFileImage(imageBytes);

            string modelID = _models[selectedModel];

            var response = await _clarifaiClient.Predict<Concept>(modelID, image)
                .ExecuteAsync();

            if (!response.IsSuccessful)
            {
                throw new Exception(response.Status.Description);
            }

            List<Concept> concepts = response.Get().Data;
            return string.Join(
                "\n",
                concepts.Select(cc => string.Format("{0} ({1:0.00}%)", cc.Name, cc.Value * 100)));
        }

        /// <summary>
        /// Takes an array of bytes that's a camera output and predicts face locations.
        /// </summary>
        /// <param name="imageBytes">the image bytes</param>
        /// <param name="actualWidth">the width of the camera output pane</param>
        /// <param name="actualHeight">the height of the camera output pane</param>
        /// <returns>the rectangles where faces are located</returns>
        public async Task<List<Rect>> PredictFaces(byte[] imageBytes, double actualWidth,
            double actualHeight)
        {
            var image = new ClarifaiFileImage(imageBytes);

            var response = await _clarifaiClient.PublicModels.FaceDetectionModel
                .Predict(image)
                .ExecuteAsync();

            if (!response.IsSuccessful)
            {
                throw new Exception("Error: " + response.Status.Description);
            }

            var rects = new List<Rect>();
            foreach (FaceDetection face in response.Get().Data)
            {
                Crop crop = face.Crop;

                double top = (double) crop.Top * actualHeight;
                double left = (double) crop.Left * actualWidth;
                double bottom = (double) crop.Bottom * actualHeight;
                double right = (double) crop.Right * actualWidth;

                double width = right - left;
                double height = bottom - top;

                rects.Add(new Rect(top, left, width, height));
            }
            return rects;
        }
    }
}
