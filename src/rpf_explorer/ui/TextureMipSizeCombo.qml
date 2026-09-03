import QtQuick

FlatComboBox {
    valueRole: "value"
    currentIndex: 2
    model: [
        { label: "1 × 1", value: 1 },
        { label: "2 × 2", value: 2 },
        { label: "4 × 4", value: 4 },
        { label: "8 × 8", value: 8 }
    ]
}
