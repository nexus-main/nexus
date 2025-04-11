#[cfg(test)]
mod tests {
    use rust_extensibility::{CatalogPath, CatalogRegistration};

    #[test]
    fn can_add() {
        // assert_eq!(add(1, 2), 3);

        // Arrange
        let catalog_path = CatalogPath::new("abc".to_string()).unwrap();

        let catalog_registration = CatalogRegistration {
            path: catalog_path,
            title: None,
            is_transient: false,
            link_target: None,
        };
    }
}
