// File picker functionality for web platform
window.pickAndReadFile = function (fileExtension) {
    return new Promise((resolve, reject) => {
        // Create a hidden file input element
        const input = document.createElement('input');
        input.type = 'file';
        input.accept = fileExtension;
        input.style.display = 'none';

        // Handle file selection
        input.onchange = async (e) => {
            const file = e.target.files[0];
            if (file) {
                try {
                    const text = await file.text();
                    resolve(text);
                } catch (error) {
                    reject(error);
                }
            } else {
                resolve(null); // User cancelled
            }
            // Clean up
            document.body.removeChild(input);
        };

        // Handle cancellation
        input.oncancel = () => {
            resolve(null);
            document.body.removeChild(input);
        };

        // Add to DOM and trigger click
        document.body.appendChild(input);
        input.click();
    });
};

// Enhanced file picker for Modelica packages
// Starts with single file selection, not directory mode
window.pickModelicaFile = function (fileExtension) {
    return new Promise((resolve, reject) => {
        // Create a hidden file input element
        const input = document.createElement('input');
        input.type = 'file';
        input.accept = fileExtension;
        input.style.display = 'none';

        // Start with single file selection (not directory mode)
        // Directory mode causes issues on some browsers

        // Handle file selection
        input.onchange = async (e) => {
            const file = e.target.files[0];
            if (file) {
                try {
                    const content = await file.text();
                    const result = {
                        Content: content,
                        IsPackageFile: file.name.toLowerCase() === 'package.mo',
                        FileName: file.name
                    };
                    resolve(JSON.stringify(result));
                } catch (error) {
                    reject(error);
                }
            } else {
                resolve(null); // User cancelled
            }
            // Clean up
            document.body.removeChild(input);
        };

        // Handle cancellation
        input.oncancel = () => {
            resolve(null);
            document.body.removeChild(input);
        };

        // Add to DOM and trigger click
        document.body.appendChild(input);
        input.click();
    });
};

// File picker for zip files containing Modelica packages
window.pickModelicaZipFile = function () {
    return new Promise((resolve, reject) => {
        // Create a hidden file input element
        const input = document.createElement('input');
        input.type = 'file';
        input.accept = '.zip';
        input.style.display = 'none';

        // Handle file selection
        input.onchange = async (e) => {
            const file = e.target.files[0];
            if (file) {
                try {
                    // Read the zip file as an array buffer
                    const arrayBuffer = await file.arrayBuffer();
                    // Convert to base64 for transfer to C#
                    const base64 = btoa(
                        new Uint8Array(arrayBuffer)
                            .reduce((data, byte) => data + String.fromCharCode(byte), '')
                    );
                    const result = {
                        ZipData: base64,
                        FileName: file.name
                    };
                    resolve(JSON.stringify(result));
                } catch (error) {
                    reject(error);
                }
            } else {
                resolve(null); // User cancelled
            }
            // Clean up
            document.body.removeChild(input);
        };

        // Handle cancellation
        input.oncancel = () => {
            resolve(null);
            document.body.removeChild(input);
        };

        // Add to DOM and trigger click
        document.body.appendChild(input);
        input.click();
    });
};

// Save a file to disk (browser download)
window.saveFile = function (filename, content) {
    const blob = new Blob([content], { type: 'text/plain;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    a.style.display = 'none';
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
};

// Save multiple files as a zip archive
window.saveZipFile = function (zipData, filename) {
    // zipData is base64 encoded zip file
    const byteCharacters = atob(zipData);
    const byteNumbers = new Array(byteCharacters.length);
    for (let i = 0; i < byteCharacters.length; i++) {
        byteNumbers[i] = byteCharacters.charCodeAt(i);
    }
    const byteArray = new Uint8Array(byteNumbers);
    const blob = new Blob([byteArray], { type: 'application/zip' });

    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    a.style.display = 'none';
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
};
