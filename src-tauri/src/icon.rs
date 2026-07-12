use base64::{engine::general_purpose, Engine};
use std::io::Cursor;
use windows::core::PCWSTR;
use windows::Win32::Graphics::Gdi::{
    DeleteObject, GetDC, GetDIBits, GetObjectW, BITMAP, BITMAPINFO,
    BITMAPINFOHEADER, DIB_RGB_COLORS, ReleaseDC,
};
use windows::Win32::UI::Shell::ExtractIconExW;
use windows::Win32::UI::WindowsAndMessaging::{DestroyIcon, GetIconInfo, HICON, ICONINFO};

/// 从 exe 文件提取图标，返回 `data:image/png;base64,...` 形式的 data URI。
/// 失败时返回 None（exe 无图标或提取失败）。
pub fn extract_icon_data_uri(exe_path: &str) -> Option<String> {
    unsafe {
        // 路径转宽字符（以 null 结尾）
        let wide: Vec<u16> = exe_path.encode_utf16().chain(std::iter::once(0)).collect();
        let pcwstr = PCWSTR(wide.as_ptr());

        // 提取大图标
        let mut hicon = HICON::default();
        let extracted = ExtractIconExW(pcwstr, 0, Some(&mut hicon as *mut HICON), None, 1);
        if extracted == 0 || hicon.is_invalid() {
            return None;
        }

        let result = extract_from_hicon(hicon);
        let _ = DestroyIcon(hicon);
        result
    }
}

/// 从 HICON 提取像素数据并编码为 PNG base64 data URI
unsafe fn extract_from_hicon(hicon: HICON) -> Option<String> {
    let mut info = ICONINFO::default();
    GetIconInfo(hicon, &mut info).ok()?;

    let hbm_color = info.hbmColor;
    let hbm_mask = info.hbmMask;
    // 颜色位图为空时退而用 mask 位图（单色图标）
    let hbm = if hbm_color.is_invalid() {
        hbm_mask
    } else {
        hbm_color
    };

    if hbm.is_invalid() {
        if !hbm_color.is_invalid() {
            let _ = DeleteObject(hbm_color.into());
        }
        if !hbm_mask.is_invalid() {
            let _ = DeleteObject(hbm_mask.into());
        }
        return None;
    }

    // 获取位图尺寸
    let mut bmp = BITMAP::default();
    let got = GetObjectW(
        hbm.into(),
        std::mem::size_of::<BITMAP>() as i32,
        Some(&mut bmp as *mut BITMAP as *mut _),
    );
    if got == 0 {
        if !hbm_color.is_invalid() {
            let _ = DeleteObject(hbm_color.into());
        }
        if !hbm_mask.is_invalid() {
            let _ = DeleteObject(hbm_mask.into());
        }
        return None;
    }

    let width = bmp.bmWidth;
    let height = bmp.bmHeight;
    if width <= 0 || height <= 0 {
        if !hbm_color.is_invalid() {
            let _ = DeleteObject(hbm_color.into());
        }
        if !hbm_mask.is_invalid() {
            let _ = DeleteObject(hbm_mask.into());
        }
        return None;
    }

    // 准备 BITMAPINFO，请求 32 位 BGRA 像素
    let mut bi = BITMAPINFO {
        bmiHeader: BITMAPINFOHEADER {
            biSize: std::mem::size_of::<BITMAPINFOHEADER>() as u32,
            biWidth: width,
            biHeight: -height, // 负值 = 自上而下扫描
            biPlanes: 1,
            biBitCount: 32,
            biCompression: 0, // BI_RGB
            biSizeImage: (width as u32) * (height as u32) * 4,
            ..Default::default()
        },
        ..Default::default()
    };

    let buf_len = (width as usize) * (height as usize) * 4;
    let mut pixels = vec![0u8; buf_len];

    let hdc = GetDC(None);
    let copied = GetDIBits(
        hdc,
        hbm,
        0,
        height as u32,
        Some(pixels.as_mut_ptr() as *mut _),
        &mut bi,
        DIB_RGB_COLORS,
    );
    let _ = ReleaseDC(None, hdc);

    // 清理 GetIconInfo 创建的位图副本
    if !hbm_color.is_invalid() {
        let _ = DeleteObject(hbm_color.into());
    }
    if !hbm_mask.is_invalid() {
        let _ = DeleteObject(hbm_mask.into());
    }

    if copied == 0 {
        return None;
    }

    // BGRA -> RGBA，并修复可能为 0 的 alpha 通道
    // （GetDIBits 返回的 32 位位图 alpha 常为 0，会导致图标完全透明）
    for chunk in pixels.chunks_mut(4) {
        chunk.swap(0, 2);
        if chunk[3] == 0 {
            chunk[3] = 255;
        }
    }

    // 编码 PNG
    let img = image::RgbaImage::from_raw(width as u32, height as u32, pixels)?;
    let mut png = Vec::new();
    image::DynamicImage::ImageRgba8(img)
        .write_to(&mut Cursor::new(&mut png), image::ImageFormat::Png)
        .ok()?;
    let b64 = general_purpose::STANDARD.encode(&png);
    Some(format!("data:image/png;base64,{b64}"))
}
