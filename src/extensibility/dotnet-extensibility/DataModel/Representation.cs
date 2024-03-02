using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Nexus.DataModel
{
    /// <summary>
    /// A representation is part of a resource.
    /// </summary>
    [DebuggerDisplay("{Id,nq}")]
    public record Representation
    {
        #region Fields

        private static readonly Regex _snakeCaseEvaluator = new("(?<=[a-z])([A-Z])", RegexOptions.Compiled);
        private static readonly HashSet<NexusDataType> _nexusDataTypeValues = new(Enum.GetValues<NexusDataType>());

        private IReadOnlyDictionary<string, JsonElement>? _parameters;

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="Representation"/>.
        /// </summary>
        /// <param name="dataType">The <see cref="NexusDataType"/>.</param>
        /// <param name="samplePeriod">The sample period.</param>
        /// <param name="parameters">An optional list of representation parameters.</param>
        /// <exception cref="ArgumentException">Thrown when the resource identifier, the sample period or the detail values are not valid.</exception>
        public Representation(
            NexusDataType dataType,
            TimeSpan samplePeriod,
            IReadOnlyDictionary<string, JsonElement>? parameters = default)
            : this(dataType, samplePeriod, parameters, RepresentationKind.Original)
        {
            //
        }

        internal Representation(
            NexusDataType dataType,
            TimeSpan samplePeriod,
            IReadOnlyDictionary<string, JsonElement>? parameters,
            RepresentationKind kind)
        {
            // data type
            if (!_nexusDataTypeValues.Contains(dataType))
                throw new ArgumentException($"The data type {dataType} is not valid.");

            DataType = dataType;

            // sample period
            if (samplePeriod.Equals(default))
                throw new ArgumentException($"The sample period {samplePeriod} is not valid.");

            SamplePeriod = samplePeriod;

            // parameters
            Parameters = parameters;

            // kind
            if (!Enum.IsDefined(typeof(RepresentationKind), kind))
                throw new ArgumentException($"The representation kind {kind} is not valid.");

            Kind = kind;

            // id
            Id = SamplePeriod.ToUnitString();

            if (kind != RepresentationKind.Original)
            {
                var snakeCaseKind = _snakeCaseEvaluator.Replace(kind.ToString(), "_$1").Trim().ToLower();
                Id = $"{Id}_{snakeCaseKind}";
            }
        }

        #endregion

        #region Properties

        /// <summary>
        /// The identifer of the representation. It is constructed using the sample period.
        /// </summary>
        [JsonIgnore]
        public string Id { get; }

        /// <summary>
        /// The data type.
        /// </summary>
        public NexusDataType DataType { get; }

        /// <summary>
        /// The sample period.
        /// </summary>
        public TimeSpan SamplePeriod { get; }

        /// <summary>
        /// The optional list of parameters.
        /// </summary>
        public IReadOnlyDictionary<string, JsonElement>? Parameters
        {
            get
            {
                return _parameters;
            }

            init
            {
                if (value is not null)
                    ValidateParameters(value);

                _parameters = value;
            }
        }

        /// <summary>
        /// The representation kind.
        /// </summary>
        internal RepresentationKind Kind { get; }

        /// <summary>
        /// The number of bits per element.
        /// </summary>
        [JsonIgnore]
        public int ElementSize => ((int)DataType & 0xFF) >> 3;

        #endregion

        #region "Methods"

        internal Representation DeepCopy()
        {
            return new Representation(
                dataType: DataType,
                samplePeriod: SamplePeriod,
                parameters: Parameters?.ToDictionary(parameter => parameter.Key, parameter => parameter.Value.Clone()),
                kind: Kind
            );
        }

        private static void ValidateParameters(IReadOnlyDictionary<string, JsonElement> parameters)
        {
            foreach (var (key, value) in parameters)
            {
                // resources and arguments have the same requirements regarding their IDs
                if (!Resource.ValidIdExpression.IsMatch(key))
                    throw new Exception("The representation argument identifier is not valid.");
            }
        }

        #endregion
    }
}