namespace PhoneMirror.Tests;

/// <summary>
/// Un H.264 de verdad hecho a mano para las pruebas: perfil baseline, CAVLC y todos los
/// macrobloques I_PCM (las muestras van tal cual, sin transformadas). Asi el descodificador de
/// Windows tiene algo real que descodificar sin depender de ningun codificador ni de ficheros de
/// fuera, y el color de cada cuadro se sabe de antemano.
/// </summary>
internal static class H264Sample
{
    private sealed class Bits
    {
        private readonly List<byte> _bytes = [];
        private int _current;
        private int _count;

        public void Bit(int bit)
        {
            _current = (_current << 1) | (bit & 1);
            if (++_count == 8)
            {
                _bytes.Add((byte)_current);
                _current = 0;
                _count = 0;
            }
        }

        public void U(int bits, int value)
        {
            for (var i = bits - 1; i >= 0; i--)
                Bit(value >> i);
        }

        public void Ue(int value)
        {
            var v = value + 1;
            var length = 0;
            for (var t = v; t > 1; t >>= 1)
                length++;
            for (var i = 0; i < length; i++)
                Bit(0);
            U(length + 1, v);
        }

        public void Se(int value) => Ue(value <= 0 ? -2 * value : 2 * value - 1);

        public bool Aligned => _count == 0;

        public void AlignZero()
        {
            while (!Aligned)
                Bit(0);
        }

        public void Trailing()
        {
            Bit(1);
            AlignZero();
        }

        public byte[] ToArray() => [.. _bytes];
    }

    /// <summary>Unidad NAL con su codigo de inicio y los bytes de prevencion de emulacion.</summary>
    private static byte[] Nal(int header, byte[] rbsp)
    {
        var output = new List<byte> { 0, 0, 0, 1, (byte)header };
        var zeros = 0;
        foreach (var b in rbsp)
        {
            if (zeros >= 2 && b <= 3)
            {
                output.Add(3);
                zeros = 0;
            }
            output.Add(b);
            zeros = b == 0 ? zeros + 1 : 0;
        }
        return [.. output];
    }

    /// <summary>SPS y PPS de un video de <paramref name="width"/>×<paramref name="height"/> (multiplos de 16), recortando <paramref name="cropBottom"/> filas (par).</summary>
    public static byte[] Config(int width, int height, int cropBottom = 0)
    {
        var sps = new Bits();
        sps.U(8, 66);       // baseline
        sps.U(8, 0);        // constraint flags
        sps.U(8, 30);       // nivel 3.0
        sps.Ue(0);          // seq_parameter_set_id
        sps.Ue(0);          // log2_max_frame_num_minus4
        sps.Ue(2);          // pic_order_cnt_type
        sps.Ue(1);          // max_num_ref_frames
        sps.Bit(0);         // gaps_in_frame_num_value_allowed_flag
        sps.Ue(width / 16 - 1);
        sps.Ue(height / 16 - 1);
        sps.Bit(1);         // frame_mbs_only_flag
        sps.Bit(1);         // direct_8x8_inference_flag
        if (cropBottom > 0)
        {
            sps.Bit(1);     // frame_cropping_flag
            sps.Ue(0);
            sps.Ue(0);
            sps.Ue(0);
            sps.Ue(cropBottom / 2);
        }
        else
        {
            sps.Bit(0);
        }
        sps.Bit(0);         // vui_parameters_present_flag
        sps.Trailing();

        var pps = new Bits();
        pps.Ue(0);          // pic_parameter_set_id
        pps.Ue(0);          // seq_parameter_set_id
        pps.Bit(0);         // entropy_coding_mode_flag (CAVLC)
        pps.Bit(0);         // bottom_field_pic_order_in_frame_present_flag
        pps.Ue(0);          // num_slice_groups_minus1
        pps.Ue(0);
        pps.Ue(0);
        pps.Bit(0);         // weighted_pred_flag
        pps.U(2, 0);        // weighted_bipred_idc
        pps.Se(0);          // pic_init_qp_minus26
        pps.Se(0);          // pic_init_qs_minus26
        pps.Se(0);          // chroma_qp_index_offset
        pps.Bit(1);         // deblocking_filter_control_present_flag
        pps.Bit(0);         // constrained_intra_pred_flag
        pps.Bit(0);         // redundant_pic_cnt_present_flag
        pps.Trailing();

        return [.. Nal(0x67, sps.ToArray()), .. Nal(0x68, pps.ToArray())];
    }

    /// <summary>Un cuadro IDR de un solo color (Y, Cb, Cr), todo en macrobloques I_PCM.</summary>
    public static byte[] Frame(int width, int height, byte y, byte cb, byte cr, int idrPicId)
    {
        var slice = new Bits();
        slice.Ue(0);        // first_mb_in_slice
        slice.Ue(7);        // slice_type: I (todas)
        slice.Ue(0);        // pic_parameter_set_id
        slice.U(4, 0);      // frame_num
        slice.Ue(idrPicId);
        slice.Bit(0);       // no_output_of_prior_pics_flag
        slice.Bit(0);       // long_term_reference_flag
        slice.Se(0);        // slice_qp_delta
        slice.Ue(1);        // disable_deblocking_filter_idc
        var mbs = width / 16 * (height / 16);
        for (var i = 0; i < mbs; i++)
        {
            slice.Ue(25);   // mb_type I_PCM
            slice.AlignZero();
            for (var k = 0; k < 256; k++)
                slice.U(8, y);
            for (var k = 0; k < 64; k++)
                slice.U(8, cb);
            for (var k = 0; k < 64; k++)
                slice.U(8, cr);
        }
        slice.Trailing();
        return Nal(0x65, slice.ToArray());
    }
}
