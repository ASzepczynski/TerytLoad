namespace TerytLoad.Pages.DbViewer
{
    /// <summary>
    /// Pojedynczy warunek filtra
    /// </summary>
    public class FilterCondition
    {
        /// <summary>Spójnik ³¹cz¹cy z poprzednim warunkiem: "AND" lub "OR" (ignorowany dla pierwszego)</summary>
        public string Connector { get; set; } = "AND";

        /// <summary>Ile nawiasów otwieraj¹cych jest przed tym warunkiem (0–2)</summary>
        public int OpenParens { get; set; } = 0;

        /// <summary>Œcie¿ka kolumny np. "Nazwa", "GminaId.Opis"</summary>
        public string Column { get; set; } = string.Empty;

        /// <summary>Operator: contains / notcontains / equals / notequals</summary>
        public string Operator { get; set; } = "contains";

        /// <summary>Wartoœæ filtra</summary>
        public string Value { get; set; } = string.Empty;

        /// <summary>Ile nawiasów zamykaj¹cych jest po tym warunku (0–2)</summary>
        public int CloseParens { get; set; } = 0;

        /// <summary>Etykieta wyœwietlana u¿ytkownikowi (np. "Gmina.Opis")</summary>
        public string ColumnLabel { get; set; } = string.Empty;

        public string OperatorLabel => Operator switch
        {
            "contains"    => "zawiera",
            "notcontains" => "nie zawiera",
            "equals"      => "=",
            "notequals"   => "?",
            _             => Operator
        };

        public string DisplayText
        {
            get
            {
                var open  = new string('(', OpenParens);
                var close = new string(')', CloseParens);
                return $"{open}{ColumnLabel} {OperatorLabel} \"{Value}\"{close}";
            }
        }
    }
}
