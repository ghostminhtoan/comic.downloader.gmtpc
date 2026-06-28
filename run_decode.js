var fso = WScript.CreateObject("Scripting.FileSystemObject");
var html = fso.OpenTextFile("page_chapter.html", 1).ReadAll();

var evalIndex = html.indexOf("eval(function(h,u,n,t,e,r)");
if (evalIndex !== -1) {
    var sub = html.substring(evalIndex);
    var endMatch = sub.match(/,\s*55\s*\)\)/);
    if (endMatch) {
        var evalBlock = sub.substring(0, endMatch.index + endMatch[0].length);
        var executableBlock = "var decoded_payload = " + evalBlock.substring(5);
        if (executableBlock.charAt(executableBlock.length - 1) === ')') {
            executableBlock = executableBlock.substring(0, executableBlock.length - 1);
        }
        
        // Polyfill for Array.prototype.reduce and Array.prototype.indexOf
        var polyfill = "if (!Array.prototype.reduce) {\n" +
            "  Array.prototype.reduce = function(callback, initialValue) {\n" +
            "    if (this == null) throw new TypeError('reduce called on null or undefined');\n" +
            "    if (typeof callback !== 'function') throw new TypeError(callback + ' is not a function');\n" +
            "    var t = Object(this), len = t.length >>> 0, k = 0, value;\n" +
            "    if (arguments.length >= 2) {\n" +
            "      value = arguments[1];\n" +
            "    } else {\n" +
            "      while (k < len && !(k in t)) { k++; }\n" +
            "      if (k >= len) throw new TypeError('Reduce of empty array with no initial value');\n" +
            "      value = t[k++];\n" +
            "    }\n" +
            "    for (; k < len; k++) {\n" +
            "      if (k in t) {\n" +
            "        value = callback(value, t[k], k, t);\n" +
            "      }\n" +
            "    }\n" +
            "    return value;\n" +
            "  };\n" +
            "}\n" +
            "if (!Array.prototype.indexOf) {\n" +
            "  Array.prototype.indexOf = function(searchElement, fromIndex) {\n" +
            "    if (this == null) throw new TypeError('indexOf called on null or undefined');\n" +
            "    var o = Object(this), len = o.length >>> 0;\n" +
            "    if (len === 0) return -1;\n" +
            "    var n = +fromIndex || 0;\n" +
            "    if (Math.abs(n) === Infinity) n = 0;\n" +
            "    if (n >= len) return -1;\n" +
            "    var k = Math.max(n >= 0 ? n : len - Math.abs(n), 0);\n" +
            "    while (k < len) {\n" +
            "      if (k in o && o[k] === searchElement) return k;\n" +
            "      k++;\n" +
            "    }\n" +
            "    return -1;\n" +
            "  };\n" +
            "}\n";

        var c42eIndex = html.indexOf("var _0xc42e");
        var c42eBlock = "";
        if (c42eIndex !== -1 && c42eIndex < evalIndex) {
            c42eBlock = html.substring(c42eIndex, evalIndex);
        }

        var runner = polyfill + c42eBlock + executableBlock + ";\n" +
            "var fso2 = WScript.CreateObject('Scripting.FileSystemObject');\n" +
            "var file = fso2.CreateTextFile('decoded_script.js', true);\n" + 
            "file.Write(decoded_payload);\n" +
            "file.Close();";
            
        var tempFile = fso.CreateTextFile("temp_eval.js", true);
        tempFile.Write(runner);
        tempFile.Close();
        
        WScript.Echo("temp_eval.js generated.");
    } else {
        WScript.Echo("Error: Could not find closing ,55))");
    }
} else {
    WScript.Echo("Error: eval(...) block not found");
}
