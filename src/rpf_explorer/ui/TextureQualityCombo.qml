import QtQuick

FlatComboBox {
    valueRole: "value"
    currentIndex: 1
    model: [
        { label: qsTr("Fast quality"), value: 0.45 },
        { label: qsTr("Balanced quality"), value: 0.7 },
        { label: qsTr("High quality"), value: 1.0 }
    ]
}
