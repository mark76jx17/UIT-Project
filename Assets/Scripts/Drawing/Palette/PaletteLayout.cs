using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Helper geometrico per disporre i controlli della palette in righe e celle,
    /// in coordinate locali al pannello (piano XY, faccia verso -Z). Padding,
    /// spacing tra righe e tra celle sono espliciti: sostituisce le coordinate
    /// "magiche" cucite a mano (origine delle vecchie sovrapposizioni).
    ///
    /// Uso tipico:
    ///   var layout = new PaletteLayout(panelSize, pad, rowGap, colGap, z);
    ///   var row = layout.Row(height);
    ///   var a = row.Left(0.05f);   // cella a sinistra, larghezza fissa
    ///   var b = row.Fill();        // riempie lo spazio rimanente (es. slider)
    ///   var c = row.Right(0.06f);  // cella ancorata a destra
    /// </summary>
    public class PaletteLayout
    {
        public readonly float InnerLeft, InnerRight, RowSpacing, ColSpacing, Z;
        public float Cursor; // bordo superiore della prossima riga (scende verso il basso)

        public PaletteLayout(Vector2 panelSize, float padding, float rowSpacing, float colSpacing, float z)
        {
            InnerLeft = -panelSize.x * 0.5f + padding;
            InnerRight = panelSize.x * 0.5f - padding;
            Cursor = panelSize.y * 0.5f - padding;
            RowSpacing = rowSpacing;
            ColSpacing = colSpacing;
            Z = z;
        }

        public float InnerWidth => InnerRight - InnerLeft;

        /// <summary>Apre una nuova riga di altezza data e fa scendere il cursore.</summary>
        public PaletteRow Row(float height)
        {
            var row = new PaletteRow(InnerLeft, InnerRight, Cursor, height, ColSpacing, Z);
            Cursor -= height + RowSpacing;
            return row;
        }

        /// <summary>Salta uno spazio verticale extra (separatore di sezione).</summary>
        public void Gap(float height) => Cursor -= height;
    }

    /// <summary>Posizione (centro locale) e dimensione di un controllo.</summary>
    public struct Cell
    {
        public Vector3 Center;
        public Vector2 Size;

        /// <summary>Divide la cella in <paramref name="count"/> sotto-celle impilate verticalmente.</summary>
        public Cell[] StackV(int count, float gap)
        {
            float h = (Size.y - (count - 1) * gap) / count;
            float topY = Center.y + Size.y * 0.5f;
            var cells = new Cell[count];
            for (int i = 0; i < count; i++)
            {
                float cy = topY - h * 0.5f - i * (h + gap);
                cells[i] = new Cell { Center = new Vector3(Center.x, cy, Center.z), Size = new Vector2(Size.x, h) };
            }
            return cells;
        }
    }

    /// <summary>
    /// Riga di layout: celle si aggiungono da sinistra (<see cref="Left"/>), da destra
    /// (<see cref="Right"/>) o riempiendo lo spazio rimanente (<see cref="Fill"/>).
    /// </summary>
    public class PaletteRow
    {
        readonly float top, height, colSpacing, z;
        float leftCursor, rightCursor;

        public PaletteRow(float left, float right, float top, float height, float colSpacing, float z)
        {
            leftCursor = left;
            rightCursor = right;
            this.top = top;
            this.height = height;
            this.colSpacing = colSpacing;
            this.z = z;
        }

        public float CenterY => top - height * 0.5f;
        public float Top => top;
        public float Height => height;
        public float Remaining => rightCursor - leftCursor;

        public Cell Left(float width) => Left(width, height);
        public Cell Left(float width, float h)
        {
            float cx = leftCursor + width * 0.5f;
            leftCursor += width + colSpacing;
            return new Cell { Center = new Vector3(cx, CenterY, z), Size = new Vector2(width, h) };
        }

        public Cell Right(float width) => Right(width, height);
        public Cell Right(float width, float h)
        {
            float cx = rightCursor - width * 0.5f;
            rightCursor -= width + colSpacing;
            return new Cell { Center = new Vector3(cx, CenterY, z), Size = new Vector2(width, h) };
        }

        /// <summary>Cella che riempie tutto lo spazio orizzontale rimasto (es. slider).</summary>
        public Cell Fill() => Fill(height);
        public Cell Fill(float h)
        {
            float w = rightCursor - leftCursor;
            float cx = (leftCursor + rightCursor) * 0.5f;
            leftCursor = rightCursor;
            return new Cell { Center = new Vector3(cx, CenterY, z), Size = new Vector2(w, h) };
        }

        /// <summary><paramref name="count"/> celle di pari larghezza che riempiono lo spazio rimasto.</summary>
        public Cell[] Split(int count) => Split(count, height);
        public Cell[] Split(int count, float h)
        {
            float w = (Remaining - (count - 1) * colSpacing) / count;
            var cells = new Cell[count];
            for (int i = 0; i < count; i++)
                cells[i] = Left(w, h);
            return cells;
        }
    }
}
